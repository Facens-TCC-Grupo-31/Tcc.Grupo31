#pragma once

#include <stdbool.h>
#include <esp_err.h>

#define APP_CONFIG_DEVICE_ID_MAX 32
#define APP_CONFIG_SSID_MAX 32
#define APP_CONFIG_PASSWORD_MAX 64
#define APP_CONFIG_MQTT_URI_MAX 128

typedef struct {
    char device_id[APP_CONFIG_DEVICE_ID_MAX + 1];
    char ssid[APP_CONFIG_SSID_MAX + 1];
    char password[APP_CONFIG_PASSWORD_MAX + 1];
    char mqtt_uri[APP_CONFIG_MQTT_URI_MAX + 1];
} app_device_config_t;

esp_err_t app_config_validate(const app_device_config_t *config);
esp_err_t app_config_save(const app_device_config_t *config);
esp_err_t app_config_load(app_device_config_t *config);
bool app_config_exists_in_nvs(void);
bool app_config_load_if_valid(app_device_config_t *config);
esp_err_t app_config_erase(void);
