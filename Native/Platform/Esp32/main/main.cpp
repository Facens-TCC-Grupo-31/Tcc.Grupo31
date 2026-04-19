#include <stdio.h>
#include <cstring>

#include "app/app_dispatcher.h"
#include "storage/config_store.h"
#include "states/provisioning_state.h"
#include "states/wifi_state.h"
#include "states/operational_state.h"
#include "states/error_state.h"

#include <freertos/FreeRTOS.h>
#include <freertos/event_groups.h>
#include <freertos/task.h>
#include <esp_check.h>
#include <esp_event.h>
#include <esp_log.h>
#include <esp_netif.h>
#include <esp_wifi.h>
#include <driver/gpio.h>
#include <nvs_flash.h>

static const char *TAG = "Sampler";

static app_context_t s_app_context = {};
static char s_wifi_ssid[APP_CONFIG_SSID_MAX + 1] = {};
static char s_wifi_password[APP_CONFIG_PASSWORD_MAX + 1] = {};

static esp_err_t connect_wifi_callback(app_context_t *context);

static void copy_string(char *destination, size_t destination_size, const char *source)
{
    if (destination == nullptr || destination_size == 0)
    {
        return;
    }

    std::strncpy(destination, source == nullptr ? "" : source, destination_size - 1);
    destination[destination_size - 1] = '\0';
}

static void apply_runtime_config(const app_device_config_t &config)
{
    copy_string(s_wifi_ssid, sizeof(s_wifi_ssid), config.ssid);
    copy_string(s_wifi_password, sizeof(s_wifi_password), config.password);
}

static void register_state_handlers()
{
    app_dispatcher_register_state(&s_app_context, APP_STATE_PROVISIONING, provisioning_state_handler());
    app_dispatcher_register_state(&s_app_context, APP_STATE_CONNECTING_WIFI, wifi_state_handler());
    app_dispatcher_register_state(&s_app_context, APP_STATE_OPERATIONAL, operational_state_handler());
    app_dispatcher_register_state(&s_app_context, APP_STATE_ERROR, error_state_handler());
}

static EventGroupHandle_t s_wifi_event_group = nullptr;
static bool s_wifi_stack_initialized = false;
static constexpr int WIFI_CONNECTED_BIT = BIT0;
static constexpr int WIFI_FAIL_BIT = BIT1;
static constexpr int MAX_WIFI_RETRIES = 10;
static int s_wifi_retry_count = 0;

#ifndef APP_FACTORY_RESET_GPIO
#define APP_FACTORY_RESET_GPIO GPIO_NUM_0
#endif

static constexpr TickType_t FACTORY_RESET_HOLD_TICKS = pdMS_TO_TICKS(3000);
static constexpr TickType_t FACTORY_RESET_POLL_TICKS = pdMS_TO_TICKS(50);

static void factory_reset_button_task(void *)
{
    bool was_pressed = false;
    bool event_posted = false;
    TickType_t press_start_ticks = 0;

    while (true)
    {
        const bool is_pressed = (gpio_get_level(APP_FACTORY_RESET_GPIO) == 0);
        const TickType_t now = xTaskGetTickCount();

        if (is_pressed)
        {
            if (!was_pressed)
            {
                was_pressed = true;
                event_posted = false;
                press_start_ticks = now;
            }

            if (!event_posted && (now - press_start_ticks) >= FACTORY_RESET_HOLD_TICKS)
            {
                event_posted = true;
                ESP_LOGW(TAG, "Factory reset long-press detected on GPIO %d", static_cast<int>(APP_FACTORY_RESET_GPIO));
                (void)app_dispatcher_post_event(&s_app_context, APP_EVENT_FACTORY_RESET);
            }
        }
        else
        {
            was_pressed = false;
            event_posted = false;
        }

        vTaskDelay(FACTORY_RESET_POLL_TICKS);
    }
}

static esp_err_t init_factory_reset_button()
{
    gpio_config_t button_config = {};
    button_config.pin_bit_mask = (1ULL << APP_FACTORY_RESET_GPIO);
    button_config.mode = GPIO_MODE_INPUT;
    button_config.pull_up_en = GPIO_PULLUP_ENABLE;
    button_config.pull_down_en = GPIO_PULLDOWN_DISABLE;
    button_config.intr_type = GPIO_INTR_DISABLE;

    ESP_RETURN_ON_ERROR(gpio_config(&button_config), TAG, "Failed to configure factory reset GPIO");

    if (xTaskCreate(factory_reset_button_task,
                    "FactoryResetBtn",
                    3072,
                    nullptr,
                    tskIDLE_PRIORITY + 1,
                    nullptr) != pdPASS)
    {
        ESP_LOGE(TAG, "Failed to create factory reset button task");
        return ESP_ERR_NO_MEM;
    }

    ESP_LOGI(TAG, "Factory reset button initialized on GPIO %d (active low, hold 3s)", static_cast<int>(APP_FACTORY_RESET_GPIO));
    return ESP_OK;
}

static void wifi_event_handler(void *, esp_event_base_t event_base, int32_t event_id, void *event_data)
{
    if (event_base == WIFI_EVENT && event_id == WIFI_EVENT_STA_START)
    {
        ESP_LOGI(TAG, "Wi-Fi station started, connecting to SSID '%s'", s_wifi_ssid);
        esp_wifi_connect();
        return;
    }

    if (event_base == WIFI_EVENT && event_id == WIFI_EVENT_STA_DISCONNECTED)
    {
        auto *event = static_cast<wifi_event_sta_disconnected_t *>(event_data);
        if (s_wifi_retry_count < MAX_WIFI_RETRIES)
        {
            ++s_wifi_retry_count;
            ESP_LOGW(TAG, "Wi-Fi disconnected (reason=%d), retry %d/%d", event->reason, s_wifi_retry_count, MAX_WIFI_RETRIES);
            esp_wifi_connect();
            return;
        }

        xEventGroupSetBits(s_wifi_event_group, WIFI_FAIL_BIT);
        (void)app_dispatcher_post_event(&s_app_context, APP_EVENT_WIFI_FAILED);
        ESP_LOGE(TAG, "Wi-Fi failed to connect to SSID '%s' after %d retries", s_wifi_ssid, MAX_WIFI_RETRIES);
        return;
    }

    if (event_base == IP_EVENT && event_id == IP_EVENT_STA_GOT_IP)
    {
        auto *event = static_cast<ip_event_got_ip_t *>(event_data);
        s_wifi_retry_count = 0;
        xEventGroupSetBits(s_wifi_event_group, WIFI_CONNECTED_BIT);
        (void)app_dispatcher_post_event(&s_app_context, APP_EVENT_WIFI_CONNECTED);
        ESP_LOGI(TAG, "Wi-Fi connected, got IP: " IPSTR ", netmask: " IPSTR ", gateway: " IPSTR,
                 IP2STR(&event->ip_info.ip), IP2STR(&event->ip_info.netmask), IP2STR(&event->ip_info.gw));
    }
}

static esp_err_t init_wifi_station()
{
    if (!s_wifi_stack_initialized)
    {
        s_wifi_event_group = xEventGroupCreate();
        if (s_wifi_event_group == nullptr)
        {
            ESP_LOGE(TAG, "Failed to create Wi-Fi event group");
            return ESP_ERR_NO_MEM;
        }

        const esp_err_t netif_err = esp_netif_init();
        if (netif_err != ESP_OK && netif_err != ESP_ERR_INVALID_STATE)
        {
            return netif_err;
        }

        const esp_err_t loop_err = esp_event_loop_create_default();
        if (loop_err != ESP_OK && loop_err != ESP_ERR_INVALID_STATE)
        {
            return loop_err;
        }

        esp_netif_create_default_wifi_sta();

        wifi_init_config_t cfg = WIFI_INIT_CONFIG_DEFAULT();
        ESP_RETURN_ON_ERROR(esp_wifi_init(&cfg), TAG, "esp_wifi_init failed");
        ESP_RETURN_ON_ERROR(esp_event_handler_instance_register(WIFI_EVENT,
                                                                 ESP_EVENT_ANY_ID,
                                                                 &wifi_event_handler,
                                                                 nullptr,
                                                                 nullptr),
                            TAG,
                            "register WIFI_EVENT handler failed");
        ESP_RETURN_ON_ERROR(esp_event_handler_instance_register(IP_EVENT,
                                                                 IP_EVENT_STA_GOT_IP,
                                                                 &wifi_event_handler,
                                                                 nullptr,
                                                                 nullptr),
                            TAG,
                            "register IP_EVENT handler failed");
        s_wifi_stack_initialized = true;
    }

    wifi_config_t wifi_config = {};
    std::strncpy(reinterpret_cast<char *>(wifi_config.sta.ssid), s_wifi_ssid, sizeof(wifi_config.sta.ssid) - 1);
    std::strncpy(reinterpret_cast<char *>(wifi_config.sta.password), s_wifi_password, sizeof(wifi_config.sta.password) - 1);
    wifi_config.sta.threshold.authmode = WIFI_AUTH_WPA2_PSK;
    wifi_config.sta.pmf_cfg.capable = true;
    wifi_config.sta.pmf_cfg.required = false;

    (void)esp_wifi_stop();
    ESP_RETURN_ON_ERROR(esp_wifi_set_mode(WIFI_MODE_STA), TAG, "esp_wifi_set_mode failed");
    ESP_RETURN_ON_ERROR(esp_wifi_set_config(WIFI_IF_STA, &wifi_config), TAG, "esp_wifi_set_config failed");
    const esp_err_t start_err = esp_wifi_start();
    if (start_err != ESP_OK && start_err != ESP_ERR_WIFI_CONN)
    {
        return start_err;
    }

    ESP_LOGI(TAG, "Wi-Fi init complete, waiting for connection...");

    const EventBits_t bits = xEventGroupWaitBits(
        s_wifi_event_group,
        WIFI_CONNECTED_BIT | WIFI_FAIL_BIT,
        pdFALSE,
        pdFALSE,
        pdMS_TO_TICKS(30000));

    if (bits & WIFI_CONNECTED_BIT)
    {
        return ESP_OK;
    }

    if (bits & WIFI_FAIL_BIT)
    {
        (void)app_dispatcher_post_event(&s_app_context, APP_EVENT_WIFI_FAILED);
        return ESP_FAIL;
    }

    ESP_LOGE(TAG, "Timed out waiting for Wi-Fi connection");
    (void)app_dispatcher_post_event(&s_app_context, APP_EVENT_WIFI_FAILED);
    return ESP_ERR_TIMEOUT;
}

static esp_err_t connect_wifi_callback(app_context_t *context)
{
    if (context != nullptr)
    {
        apply_runtime_config(context->config);
    }
    return init_wifi_station();
}

extern "C" void app_main(void)
{
    ESP_LOGI(TAG, "Starting Sampler Application...");

    esp_err_t ret = nvs_flash_init();
    if (ret == ESP_ERR_NVS_NO_FREE_PAGES || ret == ESP_ERR_NVS_NEW_VERSION_FOUND)
    {
        ESP_ERROR_CHECK(nvs_flash_erase());
        ret = nvs_flash_init();
    }
    ESP_ERROR_CHECK(ret);

    ESP_ERROR_CHECK(app_dispatcher_init(&s_app_context, 16));
    register_state_handlers();
    ESP_ERROR_CHECK(init_factory_reset_button());

    s_app_context.connect_wifi = connect_wifi_callback;

    if (app_config_load_if_valid(&s_app_context.config))
    {
        apply_runtime_config(s_app_context.config);
        app_dispatcher_transition_to(&s_app_context, APP_STATE_CONNECTING_WIFI, APP_EVENT_STARTUP);
    }
    else
    {
        app_dispatcher_transition_to(&s_app_context, APP_STATE_PROVISIONING, APP_EVENT_STARTUP);
    }

    while (1)
    {
        app_dispatcher_process_once(&s_app_context, pdMS_TO_TICKS(100));
        vTaskDelay(pdMS_TO_TICKS(100));
    }
}
