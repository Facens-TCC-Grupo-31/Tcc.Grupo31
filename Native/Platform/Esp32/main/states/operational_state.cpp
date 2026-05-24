#include "operational_state.h"

#include <memory>
#include <optional>
#include <atomic>
#include <cstring>
#include <cerrno>
#include <vector>
#include <algorithm>

#include <freertos/FreeRTOS.h>
#include <freertos/task.h>
#include <esp_log.h>
#include <esp_idf_version.h>
#include <mqtt_client.h>
#include <driver/gpio.h>
#include <esp_rom_sys.h>
#include <esp_timer.h>

#include "Reader.h"
#include "Gateway.h"
#include "EspFeeder.h"
#include "../app/app_context.h"
#include "../app/app_dispatcher.h"

static const char *TAG = "OperationalState";
static constexpr const char *REGISTRATION_TOPIC = "devices/register";
static constexpr const char *TELEMETRY_TOPIC = "devices/samples";
static constexpr int TELEMETRY_INTERVAL_MS = 5000;
static constexpr size_t BURST_SAMPLE_COUNT = 5;
static constexpr int BURST_INTER_SAMPLE_DELAY_MS = 70;
static constexpr gpio_num_t HC_SR04_TRIG_PIN = GPIO_NUM_26;
static constexpr gpio_num_t HC_SR04_ECHO_PIN = GPIO_NUM_25;

struct SampleData
{
    int distance_mm;
    short burst_sample_count;
};

class HcSr04DistanceSampler : public Reader<SampleData>
{
    enum class ReadError
    {
        None,
        EchoStuckHighBeforeTrigger,
        EchoDidNotGoHigh,
        EchoDidNotGoLow,
    };

    static constexpr int MAX_READ_ATTEMPTS = 3;
    static constexpr int64_t ECHO_WAIT_HIGH_TIMEOUT_US = 100000;
    static constexpr int64_t ECHO_WAIT_LOW_TIMEOUT_US = 60000;
    static constexpr int64_t ECHO_PRECONDITION_TIMEOUT_US = 5000;
    static constexpr uint32_t TRIG_PULSE_US = 20;
    static constexpr uint32_t POST_TRIG_SETTLE_US = 20;

    gpio_num_t trig_pin_;
    gpio_num_t echo_pin_;

    bool read_distance_mm(int &distance_mm, ReadError &error)
    {
        error = ReadError::None;

        int64_t pre_t0 = esp_timer_get_time();
        while (gpio_get_level(echo_pin_) == 1)
        {
            if (esp_timer_get_time() - pre_t0 > ECHO_PRECONDITION_TIMEOUT_US)
            {
                error = ReadError::EchoStuckHighBeforeTrigger;
                return false;
            }
        }

        gpio_set_level(trig_pin_, 0);
        esp_rom_delay_us(2);
        gpio_set_level(trig_pin_, 1);
        esp_rom_delay_us(TRIG_PULSE_US);
        gpio_set_level(trig_pin_, 0);
        esp_rom_delay_us(POST_TRIG_SETTLE_US);

        int64_t t0 = esp_timer_get_time();
        while (gpio_get_level(echo_pin_) == 0)
        {
            if (esp_timer_get_time() - t0 > ECHO_WAIT_HIGH_TIMEOUT_US)
            {
                error = ReadError::EchoDidNotGoHigh;
                return false;
            }
        }

        int64_t start = esp_timer_get_time();
        while (gpio_get_level(echo_pin_) == 1)
        {
            if (esp_timer_get_time() - start > ECHO_WAIT_LOW_TIMEOUT_US)
            {
                error = ReadError::EchoDidNotGoLow;
                return false;
            }
        }

        int64_t end = esp_timer_get_time();
        int64_t pulse_width_us = end - start;
        float distance_cm = static_cast<float>(pulse_width_us) / 58.0f;
        distance_mm = static_cast<int>(distance_cm * 10.0f);
        return distance_mm > 0;
    }

public:
    HcSr04DistanceSampler(gpio_num_t trig_pin, gpio_num_t echo_pin)
        : trig_pin_(trig_pin), echo_pin_(echo_pin)
    {
        gpio_config_t trig_cfg = {};
        trig_cfg.pin_bit_mask = (1ULL << trig_pin_);
        trig_cfg.mode = GPIO_MODE_OUTPUT;
        trig_cfg.pull_down_en = GPIO_PULLDOWN_DISABLE;
        trig_cfg.pull_up_en = GPIO_PULLUP_DISABLE;
        trig_cfg.intr_type = GPIO_INTR_DISABLE;
        ESP_ERROR_CHECK(gpio_config(&trig_cfg));

        gpio_config_t echo_cfg = {};
        echo_cfg.pin_bit_mask = (1ULL << echo_pin_);
        echo_cfg.mode = GPIO_MODE_INPUT;
        echo_cfg.pull_down_en = GPIO_PULLDOWN_DISABLE;
        echo_cfg.pull_up_en = GPIO_PULLUP_DISABLE;
        echo_cfg.intr_type = GPIO_INTR_DISABLE;
        ESP_ERROR_CHECK(gpio_config(&echo_cfg));

        esp_rom_delay_us(50000);

        ESP_LOGI(TAG, "Initialized HC-SR04 reader (TRIG=%d ECHO=%d)", static_cast<int>(trig_pin_), static_cast<int>(echo_pin_));
    }

    std::optional<SampleData> read() override
    {
        int distance_mm = 0;
        ReadError error = ReadError::None;
        bool success = false;

        for (int attempt = 1; attempt <= MAX_READ_ATTEMPTS; ++attempt)
        {
            if (read_distance_mm(distance_mm, error))
            {
                success = true;
                break;
            }

            esp_rom_delay_us(2000);
        }

        if (!success)
        {
            const int echo_level = gpio_get_level(echo_pin_);
            switch (error)
            {
            case ReadError::EchoStuckHighBeforeTrigger:
                ESP_LOGW(TAG, "Failed to read HC-SR04 distance sample: ECHO stuck HIGH before trigger after %d attempts (echo=%d)", MAX_READ_ATTEMPTS, echo_level);
                break;
            case ReadError::EchoDidNotGoHigh:
                ESP_LOGW(TAG, "Failed to read HC-SR04 distance sample: ECHO never went HIGH after trigger after %d attempts (echo=%d)", MAX_READ_ATTEMPTS, echo_level);
                break;
            case ReadError::EchoDidNotGoLow:
                ESP_LOGW(TAG, "Failed to read HC-SR04 distance sample: ECHO stayed HIGH too long after %d attempts (echo=%d)", MAX_READ_ATTEMPTS, echo_level);
                break;
            default:
                ESP_LOGW(TAG, "Failed to read HC-SR04 distance sample: unknown error after %d attempts (echo=%d)", MAX_READ_ATTEMPTS, echo_level);
                break;
            }
            return std::nullopt;
        }

        ESP_LOGI(TAG, "Sampled distance: %d mm", distance_mm);
        return SampleData
        {
            .distance_mm = distance_mm,
            .burst_sample_count = 1
        };
    }
};

class BurstMedianEspFeeder : public EspFeeder<SampleData>
{
    size_t burst_sample_count_;
    int telemetry_interval_ms_;

public:
    BurstMedianEspFeeder(
        std::unique_ptr<Reader<SampleData>> reader,
        std::unique_ptr<Gateway<SampleData>> gateway,
        size_t burst_sample_count,
        int telemetry_interval_ms)
        : EspFeeder<SampleData>(std::move(reader), std::move(gateway)),
          burst_sample_count_(burst_sample_count),
          telemetry_interval_ms_(telemetry_interval_ms)
    {
    }

protected:
    std::optional<SampleData> read_cycle_data() override
    {
        if (burst_sample_count_ == 0)
        {
            return std::nullopt;
        }

        std::vector<int> distances;
        distances.reserve(burst_sample_count_);

        for (size_t i = 0; i < burst_sample_count_; ++i)
        {
            std::optional<SampleData> sample = reader().read();
            if (!sample.has_value())
            {
                ESP_LOGW(TAG, "Burst acquisition failed at index %d", static_cast<int>(i));
                return std::nullopt;
            }

            distances.push_back(sample->distance_mm);

            if ((i + 1) < burst_sample_count_)
            {
                // HC-SR04 measurements should be spaced to avoid overlapping echoes.
                vTaskDelay(pdMS_TO_TICKS(BURST_INTER_SAMPLE_DELAY_MS));
            }
        }

        std::sort(distances.begin(), distances.end());
        const int median_distance_mm = distances[distances.size() / 2];

        ESP_LOGI(TAG, "Burst median distance: %d mm from %d samples", median_distance_mm, static_cast<int>(burst_sample_count_));

        return SampleData{
            .distance_mm = median_distance_mm,
            .burst_sample_count = static_cast<short>(burst_sample_count_)
        };
    }

    int cycle_sleep_ms() const override
    {
        return telemetry_interval_ms_;
    }
};

class MqttSampleGateway : public Gateway<SampleData>
{
    esp_mqtt_client_handle_t client_ = nullptr;
    std::atomic<bool> connected_{false};
    app_context_t *context_ = nullptr;
    char broker_uri_[APP_CONFIG_MQTT_URI_MAX + 1] = {};
    char sensor_id_[APP_CONFIG_SENSOR_ID_MAX + 1] = {};
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

            const bool hard_failure =
                err->error_type == MQTT_ERROR_TYPE_CONNECTION_REFUSED ||
                err->esp_tls_last_esp_err != 0 ||
                err->esp_transport_sock_errno == ECONNREFUSED ||
                err->esp_transport_sock_errno == EHOSTUNREACH ||
                err->esp_transport_sock_errno == ENETUNREACH ||
                err->esp_transport_sock_errno == ETIMEDOUT;

            (void)app_dispatcher_post_event(self->context_,
                                            hard_failure ? APP_EVENT_MQTT_FAILED_HARD : APP_EVENT_MQTT_FAILED);
        }
    }

public:
    explicit MqttSampleGateway(app_context_t *context)
        : context_(context)
    {
        std::strncpy(broker_uri_, context->config.mqtt_uri, sizeof(broker_uri_) - 1);
        std::strncpy(sensor_id_, context->config.sensor_id, sizeof(sensor_id_) - 1);
        has_registration_token_ = context->has_registration_token;
        if (has_registration_token_)
        {
            std::strncpy(registration_token_, context->registration_token, sizeof(registration_token_) - 1);
        }

        esp_mqtt_client_config_t config = {};
        config.broker.address.uri = broker_uri_;
    #if ESP_IDF_VERSION >= ESP_IDF_VERSION_VAL(5, 0, 0)
        config.network.disable_auto_reconnect = true;
    #else
        config.disable_auto_reconnect = true;
    #endif

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

    void send(const SampleData &data) override
    {
        if (!connected_.load())
        {
            ESP_LOGW(TAG, "Skipping publish because MQTT client is not connected yet");
            return;
        }

        if (has_registration_token_)
        {
            char registration_payload[APP_CONFIG_SENSOR_ID_MAX + APP_RUNTIME_TOKEN_MAX + 128] = {};
            std::snprintf(registration_payload,
                          sizeof(registration_payload),
                          "{\"sensorId\":%s,\"provisioningToken\":\"%s\",\"baselineDistanceMm\":%d,\"calibrationSampleCount\":%d}",
                          sensor_id_,
                          registration_token_,
                          data.distance_mm,
                          static_cast<int>(data.burst_sample_count));

            const int registration_message_id = esp_mqtt_client_publish(client_, REGISTRATION_TOPIC, registration_payload, 0, 1, 0);
            if (registration_message_id >= 0)
            {
                has_registration_token_ = false;
                if (context_ != nullptr)
                {
                    context_->has_registration_token = false;
                    context_->registration_token[0] = '\0';
                    context_->config.registration_token[0] = '\0';
                    const esp_err_t persist_err = app_config_save(&context_->config);
                    if (persist_err != ESP_OK)
                    {
                        ESP_LOGE(TAG, "Failed to clear persisted registration token: %s", esp_err_to_name(persist_err));
                    }
                }
                ESP_LOGI(TAG, "Published one-shot registration for sensor %s", sensor_id_);
                return;
            }

            ESP_LOGE(TAG, "Failed to publish one-shot registration for sensor %s", sensor_id_);
            return;
        }

        char telemetry_payload[APP_CONFIG_SENSOR_ID_MAX + 48] = {};
        std::snprintf(telemetry_payload,
                      sizeof(telemetry_payload),
                  "{\"sensorId\":%s,\"distanceMm\":%d}",
                      sensor_id_,
                  data.distance_mm);

        const int message_id = esp_mqtt_client_publish(client_, TELEMETRY_TOPIC, telemetry_payload, 0, 1, 0);

        if (message_id >= 0)
        {
            ESP_LOGI(TAG, "Published telemetry distance %d mm for sensor %s", data.distance_mm, sensor_id_);
            return;
        }

        ESP_LOGE(TAG, "Failed to publish telemetry distance %d mm for sensor %s", data.distance_mm, sensor_id_);
    }
};

static std::unique_ptr<EspFeeder<SampleData>> s_feeder;

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

    auto sampler = std::make_unique<HcSr04DistanceSampler>(HC_SR04_TRIG_PIN, HC_SR04_ECHO_PIN);
    auto gateway = std::make_unique<MqttSampleGateway>(context);
    s_feeder = std::make_unique<BurstMedianEspFeeder>(
        std::move(sampler),
        std::move(gateway),
        BURST_SAMPLE_COUNT,
        TELEMETRY_INTERVAL_MS
    );
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
