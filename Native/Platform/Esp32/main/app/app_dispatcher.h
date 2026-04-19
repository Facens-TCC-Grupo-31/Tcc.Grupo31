#pragma once

#include <esp_err.h>

#include "app_context.h"

esp_err_t app_dispatcher_init(app_context_t *context, uint32_t queue_depth);
void app_dispatcher_register_state(app_context_t *context, app_state_t state, state_handler_t handler);
esp_err_t app_dispatcher_post_event(app_context_t *context, app_event_t event);
void app_dispatcher_transition_to(app_context_t *context, app_state_t next_state, app_event_t reason);
void app_dispatcher_process_once(app_context_t *context, TickType_t timeout_ticks);
