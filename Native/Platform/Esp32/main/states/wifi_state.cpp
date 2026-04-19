#include "wifi_state.h"

#include <esp_log.h>

#include "../app/app_context.h"

static const char *TAG = "WifiState";
static bool s_attempted_connect = false;

static void enter(app_context_t *)
{
    s_attempted_connect = false;
    ESP_LOGI(TAG, "Entering Wi-Fi connection state");
}

static void run(app_context_t *context)
{
    if (context == nullptr || s_attempted_connect)
    {
        return;
    }

    s_attempted_connect = true;
    if (context->connect_wifi == nullptr)
    {
        ESP_LOGE(TAG, "connect_wifi callback is not configured");
        return;
    }

    const esp_err_t err = context->connect_wifi(context);
    if (err != ESP_OK)
    {
        ESP_LOGE(TAG, "Wi-Fi connect failed: %s", esp_err_to_name(err));
    }
}

static void exit(app_context_t *)
{
    ESP_LOGI(TAG, "Exiting Wi-Fi connection state");
}

state_handler_t wifi_state_handler(void)
{
    state_handler_t handler = {};
    handler.enter = enter;
    handler.run = run;
    handler.exit = exit;
    return handler;
}
