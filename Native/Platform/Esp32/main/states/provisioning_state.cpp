#include "provisioning_state.h"

#include <cstring>

#include <esp_log.h>

#include "../app/app_context.h"
#include "../app/app_dispatcher.h"
#include "../provisioning/provisioning_transport.h"

static const char *TAG = "ProvisioningState";

static void copy_string(char *destination, size_t destination_size, const char *source)
{
    if (destination == nullptr || destination_size == 0)
    {
        return;
    }

    std::strncpy(destination, source == nullptr ? "" : source, destination_size - 1);
    destination[destination_size - 1] = '\0';
}

static void enter(app_context_t *)
{
    ESP_LOGI(TAG, "Entering provisioning state");
    const esp_err_t err = provisioning_transport_start_ble();
    if (err != ESP_OK)
    {
        ESP_LOGE(TAG, "Failed to start BLE provisioning transport: %s — device is unprovisioned and BLE advertising is not active",
                 esp_err_to_name(err));
    }
}

static void run(app_context_t *context)
{
    if (context == nullptr)
    {
        return;
    }

    provisioning_payload_t payload = {};
    if (!provisioning_transport_try_get_payload(&payload))
    {
        return;
    }

    const esp_err_t save_err = app_config_save(&payload.config);
    if (save_err != ESP_OK)
    {
        ESP_LOGE(TAG, "Rejected provisioning payload: %s", esp_err_to_name(save_err));
        return;
    }

    context->config = payload.config;
    context->has_registration_token = payload.has_token;
    context->registration_token[0] = '\0';
    if (payload.has_token)
    {
        copy_string(context->registration_token, sizeof(context->registration_token), payload.token);
    }

    ESP_LOGI(TAG, "Provisioning complete for device %s", context->config.device_id);
    if (app_dispatcher_post_event(context, APP_EVENT_PROVISIONING_DONE) != ESP_OK)
    {
        ESP_LOGE(TAG, "Failed to post APP_EVENT_PROVISIONING_DONE");
    }
}

static void exit(app_context_t *)
{
    (void)provisioning_transport_stop_ble();
    ESP_LOGI(TAG, "Exiting provisioning state");
}

state_handler_t provisioning_state_handler(void)
{
    state_handler_t handler = {};
    handler.enter = enter;
    handler.run = run;
    handler.exit = exit;
    return handler;
}
