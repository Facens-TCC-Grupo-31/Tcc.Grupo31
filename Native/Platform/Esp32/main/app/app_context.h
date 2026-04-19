#pragma once

#include <freertos/FreeRTOS.h>
#include <freertos/queue.h>
#include <esp_err.h>

#include "app_events.h"
#include "app_state.h"
#include "../storage/config_store.h"

#define APP_RUNTIME_TOKEN_MAX 128

typedef struct app_context_t {
    app_state_t current_state;
    QueueHandle_t event_queue;
    state_handler_t handlers[APP_STATE_MAX];
    app_device_config_t config;
    bool has_registration_token;
    char registration_token[APP_RUNTIME_TOKEN_MAX + 1];
    TickType_t error_enter_tick;
    esp_err_t (*connect_wifi)(struct app_context_t *context);
} app_context_t;
