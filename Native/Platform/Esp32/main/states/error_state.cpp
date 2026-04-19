#include "error_state.h"

#include <freertos/FreeRTOS.h>
#include <freertos/task.h>
#include <esp_log.h>

#include "../app/app_context.h"
#include "../app/app_dispatcher.h"

static const char *TAG = "ErrorState";
static constexpr TickType_t RETRY_DELAY_TICKS = pdMS_TO_TICKS(5000);
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
