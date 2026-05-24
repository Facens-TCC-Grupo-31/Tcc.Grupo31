#include "error_state.h"

#include <freertos/FreeRTOS.h>
#include <freertos/task.h>
#include <esp_log.h>

#include "../app/app_context.h"
#include "../app/app_dispatcher.h"

static const char *TAG = "ErrorState";
static constexpr TickType_t RETRY_DELAY_TICKS = pdMS_TO_TICKS(5000);
static constexpr uint8_t MAX_FAILURE_CYCLES_BEFORE_RESET = 3;
static bool s_retry_event_sent = false;

static void enter(app_context_t *context)
{
    s_retry_event_sent = false;
    if (context != nullptr)
    {
        context->error_enter_tick = xTaskGetTickCount();
    }
    ESP_LOGE(TAG, "Entering error state");
}

static void run(app_context_t *context)
{
    if (context == nullptr || s_retry_event_sent)
    {
        return;
    }

    const TickType_t elapsed = xTaskGetTickCount() - context->error_enter_tick;
    if (elapsed >= RETRY_DELAY_TICKS)
    {
        s_retry_event_sent = true;

        const bool should_reset_for_wifi =
            context->last_error_cause == APP_ERROR_CAUSE_WIFI &&
            context->wifi_failure_cycles >= MAX_FAILURE_CYCLES_BEFORE_RESET;
        const bool should_reset_for_mqtt =
            context->last_error_cause == APP_ERROR_CAUSE_MQTT_HARD &&
            context->mqtt_hard_failure_cycles >= MAX_FAILURE_CYCLES_BEFORE_RESET;

        if (should_reset_for_wifi || should_reset_for_mqtt)
        {
            ESP_LOGW(TAG,
                     "Failure threshold reached (cause=%d, wifi_cycles=%u, mqtt_hard_cycles=%u). Resetting to provisioning.",
                     static_cast<int>(context->last_error_cause),
                     context->wifi_failure_cycles,
                     context->mqtt_hard_failure_cycles);
            (void)app_dispatcher_post_event(context, APP_EVENT_RESET_TO_PROVISIONING);
            return;
        }

        (void)app_dispatcher_post_event(context, APP_EVENT_TIMEOUT);
    }
}

static void exit(app_context_t *)
{
    ESP_LOGI(TAG, "Exiting error state");
}

state_handler_t error_state_handler(void)
{
    state_handler_t handler = {};
    handler.enter = enter;
    handler.run = run;
    handler.exit = exit;
    return handler;
}
