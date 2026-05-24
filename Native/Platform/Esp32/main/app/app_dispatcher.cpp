#include "app_dispatcher.h"

#include <esp_check.h>
#include <esp_log.h>
#include <esp_system.h>

#include "../storage/config_store.h"

static const char *TAG = "AppFsm";
static constexpr uint8_t MAX_FAILURE_CYCLES_BEFORE_RESET = 3;

static const char *state_to_string(app_state_t state)
{
    switch (state)
    {
    case APP_STATE_UNPROVISIONED:
        return "UNPROVISIONED";
    case APP_STATE_PROVISIONING:
        return "PROVISIONING";
    case APP_STATE_CONNECTING_WIFI:
        return "CONNECTING_WIFI";
    case APP_STATE_OPERATIONAL:
        return "OPERATIONAL";
    case APP_STATE_ERROR:
        return "ERROR";
    default:
        return "UNKNOWN";
    }
}

static void handle_event(app_context_t *context, app_event_t event)
{
    if (context == nullptr)
    {
        return;
    }

    if (event == APP_EVENT_FACTORY_RESET)
    {
        ESP_LOGD(TAG, "Factory reset event received");
        ESP_ERROR_CHECK(app_config_erase());
        esp_restart();
        return;
    }

    if (event == APP_EVENT_RESET_TO_PROVISIONING)
    {
        ESP_LOGW(TAG,
                 "Resetting to provisioning (wifi failure cycles=%u, mqtt hard failure cycles=%u)",
                 context->wifi_failure_cycles,
                 context->mqtt_hard_failure_cycles);
        ESP_ERROR_CHECK(app_config_erase());
        esp_restart();
        return;
    }

    switch (context->current_state)
    {
    case APP_STATE_UNPROVISIONED:
    case APP_STATE_PROVISIONING:
        if (event == APP_EVENT_PROVISIONING_DONE)
        {
            context->wifi_failure_cycles = 0;
            context->mqtt_hard_failure_cycles = 0;
            context->last_error_cause = APP_ERROR_CAUSE_NONE;
            app_dispatcher_transition_to(context, APP_STATE_CONNECTING_WIFI, event);
        }
        break;

    case APP_STATE_CONNECTING_WIFI:
        if (event == APP_EVENT_WIFI_CONNECTED)
        {
            context->wifi_failure_cycles = 0;
            context->last_error_cause = APP_ERROR_CAUSE_NONE;
            app_dispatcher_transition_to(context, APP_STATE_OPERATIONAL, event);
        }
        else if (event == APP_EVENT_WIFI_FAILED)
        {
            if (context->wifi_failure_cycles < 255)
            {
                ++context->wifi_failure_cycles;
            }
            context->last_error_cause = APP_ERROR_CAUSE_WIFI;
            ESP_LOGW(TAG,
                     "Wi-Fi failure cycle %u/%u",
                     context->wifi_failure_cycles,
                     MAX_FAILURE_CYCLES_BEFORE_RESET);
            app_dispatcher_transition_to(context, APP_STATE_ERROR, event);
        }
        break;

    case APP_STATE_OPERATIONAL:
        if (event == APP_EVENT_MQTT_CONNECTED)
        {
            context->mqtt_hard_failure_cycles = 0;
            context->last_error_cause = APP_ERROR_CAUSE_NONE;
        }
        else if (event == APP_EVENT_WIFI_FAILED || event == APP_EVENT_MQTT_FAILED)
        {
            app_dispatcher_transition_to(context, APP_STATE_ERROR, event);
        }
        else if (event == APP_EVENT_MQTT_FAILED_HARD)
        {
            if (context->mqtt_hard_failure_cycles < 255)
            {
                ++context->mqtt_hard_failure_cycles;
            }
            context->last_error_cause = APP_ERROR_CAUSE_MQTT_HARD;
            ESP_LOGW(TAG,
                     "MQTT hard failure cycle %u/%u",
                     context->mqtt_hard_failure_cycles,
                     MAX_FAILURE_CYCLES_BEFORE_RESET);
            app_dispatcher_transition_to(context, APP_STATE_ERROR, event);
        }
        break;

    case APP_STATE_ERROR:
        if (event == APP_EVENT_RETRY_NEXT_ATTEMPT)
        {
            app_dispatcher_transition_to(context, APP_STATE_CONNECTING_WIFI, event);
        }
        break;

    default:
        break;
    }
}

esp_err_t app_dispatcher_init(app_context_t *context, uint32_t queue_depth)
{
    if (context == nullptr)
    {
        return ESP_ERR_INVALID_ARG;
    }

    *context = {};
    context->current_state = APP_STATE_UNPROVISIONED;
    context->event_queue = xQueueCreate(queue_depth, sizeof(app_event_t));
    if (context->event_queue == nullptr)
    {
        return ESP_ERR_NO_MEM;
    }

    return ESP_OK;
}

void app_dispatcher_register_state(app_context_t *context, app_state_t state, state_handler_t handler)
{
    if (context == nullptr || state < APP_STATE_UNPROVISIONED || state >= APP_STATE_MAX)
    {
        return;
    }

    context->handlers[state] = handler;
}

esp_err_t app_dispatcher_post_event(app_context_t *context, app_event_t event)
{
    if (context == nullptr || context->event_queue == nullptr)
    {
        return ESP_ERR_INVALID_STATE;
    }

    if (xQueueSend(context->event_queue, &event, 0) != pdPASS)
    {
        return ESP_ERR_TIMEOUT;
    }

    return ESP_OK;
}

void app_dispatcher_transition_to(app_context_t *context, app_state_t next_state, app_event_t)
{
    if (context == nullptr || next_state < APP_STATE_UNPROVISIONED || next_state >= APP_STATE_MAX)
    {
        return;
    }

    if (next_state == context->current_state)
    {
        return;
    }

    const app_state_t previous = context->current_state;
    auto &previous_handler = context->handlers[previous];
    if (previous_handler.exit != nullptr)
    {
        previous_handler.exit(context);
    }

    context->current_state = next_state;

    ESP_LOGD(TAG, "Transition %s -> %s", state_to_string(previous), state_to_string(next_state));

    auto &next_handler = context->handlers[next_state];
    if (next_handler.enter != nullptr)
    {
        next_handler.enter(context);
    }
}

void app_dispatcher_process_once(app_context_t *context, TickType_t timeout_ticks)
{
    if (context == nullptr)
    {
        return;
    }

    app_event_t event = APP_EVENT_TIMEOUT;
    if (context->event_queue != nullptr && xQueueReceive(context->event_queue, &event, timeout_ticks) == pdPASS)
    {
        handle_event(context, event);
    }

    auto &handler = context->handlers[context->current_state];
    if (handler.run != nullptr)
    {
        handler.run(context);
    }
}
