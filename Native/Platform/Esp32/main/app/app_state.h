#pragma once

struct app_context_t;

typedef enum {
    APP_STATE_UNPROVISIONED = 0,
    APP_STATE_PROVISIONING,
    APP_STATE_CONNECTING_WIFI,
    APP_STATE_OPERATIONAL,
    APP_STATE_ERROR,
    APP_STATE_MAX
} app_state_t;

typedef struct {
    void (*enter)(app_context_t *context);
    void (*run)(app_context_t *context);
    void (*exit)(app_context_t *context);
} state_handler_t;
