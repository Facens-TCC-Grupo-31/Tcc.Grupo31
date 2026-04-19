#include "config_store.h"

#include <cstring>

#include <esp_check.h>
#include <nvs.h>

static constexpr const char *NVS_NAMESPACE = "app_cfg";
static constexpr const char *KEY_PROVISIONED = "prov";
static constexpr const char *KEY_DEVICE_ID = "dev_id";
static constexpr const char *KEY_SSID = "ssid";
static constexpr const char *KEY_PASSWORD = "pass";
static constexpr const char *KEY_MQTT_URI = "mqtt_uri";

static void copy_string(char *destination, size_t destination_size, const char *source)
{
    if (destination == nullptr || destination_size == 0)
    {
        return;
    }

    std::strncpy(destination, source == nullptr ? "" : source, destination_size - 1);
    destination[destination_size - 1] = '\0';
}

bool app_config_exists_in_nvs(void)
{
    nvs_handle_t handle;
    if (nvs_open(NVS_NAMESPACE, NVS_READONLY, &handle) != ESP_OK)
    {
        return false;
    }

    uint8_t provisioned = 0;
    const esp_err_t err = nvs_get_u8(handle, KEY_PROVISIONED, &provisioned);
    nvs_close(handle);

    return err == ESP_OK && provisioned == 1;
}

esp_err_t app_config_validate(const app_device_config_t *config)
{
    if (config == nullptr)
    {
        return ESP_ERR_INVALID_ARG;
    }

    if (config->device_id[0] == '\0' || config->ssid[0] == '\0' || config->mqtt_uri[0] == '\0')
    {
        return ESP_ERR_INVALID_ARG;
    }

    return ESP_OK;
}

esp_err_t app_config_save(const app_device_config_t *config)
{
    ESP_RETURN_ON_ERROR(app_config_validate(config), "ConfigStore", "Invalid config");

    nvs_handle_t handle;
    ESP_RETURN_ON_ERROR(nvs_open(NVS_NAMESPACE, NVS_READWRITE, &handle), "ConfigStore", "nvs_open failed");

    esp_err_t err = nvs_set_str(handle, KEY_DEVICE_ID, config->device_id);
    if (err == ESP_OK)
    {
        err = nvs_set_str(handle, KEY_SSID, config->ssid);
    }
    if (err == ESP_OK)
    {
        err = nvs_set_str(handle, KEY_PASSWORD, config->password);
    }
    if (err == ESP_OK)
    {
        err = nvs_set_str(handle, KEY_MQTT_URI, config->mqtt_uri);
    }
    if (err == ESP_OK)
    {
        err = nvs_set_u8(handle, KEY_PROVISIONED, 1);
    }
    if (err == ESP_OK)
    {
        err = nvs_commit(handle);
    }

    nvs_close(handle);
    return err;
}

esp_err_t app_config_load(app_device_config_t *config)
{
    if (config == nullptr)
    {
        return ESP_ERR_INVALID_ARG;
    }

    nvs_handle_t handle;
    ESP_RETURN_ON_ERROR(nvs_open(NVS_NAMESPACE, NVS_READONLY, &handle), "ConfigStore", "nvs_open failed");

    *config = {};

    size_t device_id_len = sizeof(config->device_id);
    size_t ssid_len = sizeof(config->ssid);
    size_t password_len = sizeof(config->password);
    size_t mqtt_uri_len = sizeof(config->mqtt_uri);

    esp_err_t err = nvs_get_str(handle, KEY_DEVICE_ID, config->device_id, &device_id_len);
    if (err == ESP_OK)
    {
        err = nvs_get_str(handle, KEY_SSID, config->ssid, &ssid_len);
    }
    if (err == ESP_OK)
    {
        err = nvs_get_str(handle, KEY_PASSWORD, config->password, &password_len);
    }
    if (err == ESP_OK)
    {
        err = nvs_get_str(handle, KEY_MQTT_URI, config->mqtt_uri, &mqtt_uri_len);
    }

    nvs_close(handle);

    if (err == ESP_OK)
    {
        copy_string(config->device_id, sizeof(config->device_id), config->device_id);
        copy_string(config->ssid, sizeof(config->ssid), config->ssid);
        copy_string(config->password, sizeof(config->password), config->password);
        copy_string(config->mqtt_uri, sizeof(config->mqtt_uri), config->mqtt_uri);

        return app_config_validate(config);
    }

    return err;
}

bool app_config_load_if_valid(app_device_config_t *config)
{
    if (config == nullptr)
    {
        return false;
    }

    if (!app_config_exists_in_nvs())
    {
        return false;
    }

    return app_config_load(config) == ESP_OK;
}

esp_err_t app_config_erase(void)
{
    nvs_handle_t handle;
    ESP_RETURN_ON_ERROR(nvs_open(NVS_NAMESPACE, NVS_READWRITE, &handle), "ConfigStore", "nvs_open failed");

    const esp_err_t err = nvs_erase_all(handle);
    if (err == ESP_OK)
    {
        ESP_RETURN_ON_ERROR(nvs_commit(handle), "ConfigStore", "nvs_commit failed");
    }

    nvs_close(handle);
    return err;
}
