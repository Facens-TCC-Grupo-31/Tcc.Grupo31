#include "operational_state.h"

#include <memory>
#include <optional>
#include <atomic>
#include <cstring>

#include <freertos/FreeRTOS.h>
#include <freertos/task.h>
#include <esp_log.h>
#include <mqtt_client.h>

#include "Reader.h"
#include "Gateway.h"
#include "EspFeeder.h"
#include "ReadingDto.h"
#include "ProvisioningDto.h"
#include "../app/app_context.h"
#include "../app/app_dispatcher.h"

static const char *TAG = "OperationalState";
static constexpr const char *REGISTRATION_TOPIC = "devices/register";
static constexpr const char *TELEMETRY_TOPIC = "devices/samples";

class SimpleSampler : public Reader<ReadingDto>
{
    int counter = 0;
    std::string device_id_;

public:
    explicit SimpleSampler(const char *device_id) : device_id_(device_id) {}

    std::optional<ReadingDto> read() override
    {
        ReadingDto data;
        data.sensorId = device_id_;
        data.fillLevel = static_cast<float>(counter++);
        ESP_LOGI(TAG, "Sampled fill level: %.2f for sensor: %s", data.fillLevel, data.sensorId.c_str());
        return data;
    }
};

class MqttSampleGateway : public Gateway<ReadingDto>
{
    esp_mqtt_client_handle_t client_ = nullptr;
    std::atomic<bool> connected_{false};
    app_context_t *context_ = nullptr;
    char broker_uri_[APP_CONFIG_MQTT_URI_MAX + 1] = {};
    char device_id_[APP_CONFIG_DEVICE_ID_MAX + 1] = {};
    char registration_token_[APP_RUNTIME_TOKEN_MAX + 1] = {};
    bool has_registration_token_ = false;

    static void mqtt_event_handler(void *handler_args, esp_event_base_t, int32_t event_id, void *event_data)
    {
        auto *self = static_cast<MqttSampleGateway *>(handler_args);
        if (self == nullptr)
        {
            return;
        }

        if (event_id == MQTT_EVENT_CONNECTED)
        {
            self->connected_.store(true);
            (void)app_dispatcher_post_event(self->context_, APP_EVENT_MQTT_CONNECTED);
            ESP_LOGI(TAG, "Connected to MQTT broker at %s", self->broker_uri_);
            return;
        }

        if (event_id == MQTT_EVENT_DISCONNECTED)
        {
            self->connected_.store(false);
            (void)app_dispatcher_post_event(self->context_, APP_EVENT_MQTT_FAILED);
            ESP_LOGW(TAG, "Disconnected from MQTT broker at %s", self->broker_uri_);
            return;
        }

        if (event_id == MQTT_EVENT_ERROR)
        {
            auto *event = static_cast<esp_mqtt_event_handle_t>(event_data);
            if (event == nullptr || event->error_handle == nullptr)
            {
                ESP_LOGE(TAG, "MQTT_EVENT_ERROR received without error details");
                return;
            }

            const auto *err = event->error_handle;
            ESP_LOGE(TAG,
                     "MQTT transport error: type=%d, esp_tls_err=0x%x, tls_stack_err=0x%x, sock_errno=%d (%s)",
                     err->error_type,
                     err->esp_tls_last_esp_err,
                     err->esp_tls_stack_err,
                     err->esp_transport_sock_errno,
                     std::strerror(err->esp_transport_sock_errno));
            if (err->error_type == MQTT_ERROR_TYPE_CONNECTION_REFUSED)
            {
                ESP_LOGE(TAG, "MQTT connection refused, return code=%d", err->connect_return_code);
            }

            (void)app_dispatcher_post_event(self->context_, APP_EVENT_MQTT_FAILED);
        }
    }

public:
    explicit MqttSampleGateway(app_context_t *context)
        : context_(context)
    {
        std::strncpy(broker_uri_, context->config.mqtt_uri, sizeof(broker_uri_) - 1);
        std::strncpy(device_id_, context->config.device_id, sizeof(device_id_) - 1);
        has_registration_token_ = context->has_registration_token;
        if (has_registration_token_)
        {
            std::strncpy(registration_token_, context->registration_token, sizeof(registration_token_) - 1);
        }

        esp_mqtt_client_config_t config = {};
        config.broker.address.uri = broker_uri_;

        ESP_LOGI(TAG, "Initializing MQTT client for broker %s", broker_uri_);

        client_ = esp_mqtt_client_init(&config);
        esp_mqtt_client_register_event(client_, MQTT_EVENT_ANY, mqtt_event_handler, this);
        esp_mqtt_client_start(client_);
    }

    ~MqttSampleGateway() override
    {
        if (client_ != nullptr)
        {
            esp_mqtt_client_stop(client_);
            esp_mqtt_client_destroy(client_);
        }
    }

    void send(const ReadingDto &data) override
    {
        if (!connected_.load())
        {
            ESP_LOGW(TAG, "Skipping publish because MQTT client is not connected yet");
            return;
        }

        if (has_registration_token_)
        {
            ProvisioningDto registration;
            registration.deviceId = device_id_;
            registration.token = registration_token_;

            char registration_payload[APP_CONFIG_DEVICE_ID_MAX + APP_RUNTIME_TOKEN_MAX + 48] = {};
            std::snprintf(registration_payload,
                          sizeof(registration_payload),
                          "{\"deviceId\":\"%s\",\"token\":\"%s\"}",
                          registration.deviceId.c_str(),
                          registration.token.c_str());

            const int registration_message_id = esp_mqtt_client_publish(client_, REGISTRATION_TOPIC, registration_payload, 0, 1, 0);
            if (registration_message_id >= 0)
            {
                has_registration_token_ = false;
                if (context_ != nullptr)
                {
                    context_->has_registration_token = false;
                    context_->registration_token[0] = '\0';
                }
                ESP_LOGI(TAG, "Published one-shot registration for device %s", device_id_);
                return;
            }

            ESP_LOGE(TAG, "Failed to publish one-shot registration for device %s", device_id_);
            return;
        }

        char telemetry_payload[APP_CONFIG_DEVICE_ID_MAX + 64] = {};
        std::snprintf(telemetry_payload,
                      sizeof(telemetry_payload),
                      "{\"sensorId\":\"%s\",\"fillLevel\":%.2f}",
                      data.sensorId.c_str(),
                      data.fillLevel);

        const int message_id = esp_mqtt_client_publish(client_, TELEMETRY_TOPIC, telemetry_payload, 0, 1, 0);

        if (message_id >= 0)
        {
            ESP_LOGI(TAG, "Published fill level %.2f for sensor %s", data.fillLevel, data.sensorId.c_str());
            return;
        }

        ESP_LOGE(TAG, "Failed to publish fill level %.2f for sensor %s", data.fillLevel, data.sensorId.c_str());
    }
};

static std::unique_ptr<EspFeeder<ReadingDto>> s_feeder;

static void enter(app_context_t *)
{
    ESP_LOGI(TAG, "Entering operational state");
}

static void run(app_context_t *context)
{
    if (context == nullptr || s_feeder != nullptr)
    {
        return;
    }

    auto sampler = std::make_unique<SimpleSampler>(context->config.device_id);
    auto gateway = std::make_unique<MqttSampleGateway>(context);
    s_feeder = std::make_unique<EspFeeder<ReadingDto>>(std::move(sampler), std::move(gateway));
    s_feeder->start();
    ESP_LOGI(TAG, "Operational workload started");
}

static void exit(app_context_t *)
{
    if (s_feeder != nullptr)
    {
        s_feeder->stop();
        s_feeder.reset();
    }
    ESP_LOGI(TAG, "Exiting operational state");
}

state_handler_t operational_state_handler(void)
{
    state_handler_t handler = {};
    handler.enter = enter;
    handler.run = run;
    handler.exit = exit;
    return handler;
}
