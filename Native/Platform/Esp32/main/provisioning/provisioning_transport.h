#pragma once

#include <stdbool.h>
#include <esp_err.h>

#include "../storage/config_store.h"

#define APP_PROVISION_TOKEN_MAX 128

typedef struct {
    app_device_config_t config;
    bool has_token;
    char token[APP_PROVISION_TOKEN_MAX + 1];
} provisioning_payload_t;

esp_err_t provisioning_transport_start_ble(void);
esp_err_t provisioning_transport_stop_ble(void);
bool provisioning_transport_try_get_payload(provisioning_payload_t *payload);

// Temporary integration seam for BLE callback glue. The real BLE GATT/protocomm
// handler should call this once a full payload is parsed and validated for shape.
esp_err_t provisioning_transport_submit_ble_payload(const provisioning_payload_t *payload);
