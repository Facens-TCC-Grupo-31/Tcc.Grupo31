#include <memory>

#include "ConsoleGateway.h"
#include "HC_SR04_Reader.h"

#include "EspFeeder.h"

#include "driver/gpio.h"
#include "esp_log.h"
#include "nvs_flash.h"

static const char *TAG = "HC_SR04_Test";

// Adjust these for your setup.
static constexpr gpio_num_t TRIG_PIN = GPIO_NUM_26;
static constexpr gpio_num_t ECHO_PIN = GPIO_NUM_25;
static constexpr float CONTAINER_DEPTH_CM = 20.0f;
static constexpr const char *SENSOR_ID = "hc-sr04-calibration";

extern "C" void app_main(void)
{
    ESP_LOGI(TAG, "Starting isolated HC-SR04 calibration project");

    esp_err_t ret = nvs_flash_init();
    if (ret == ESP_ERR_NVS_NO_FREE_PAGES || ret == ESP_ERR_NVS_NEW_VERSION_FOUND)
    {
        ESP_ERROR_CHECK(nvs_flash_erase());
        ret = nvs_flash_init();
    }
    ESP_ERROR_CHECK(ret);

    auto reader = std::make_unique<HC_SR04_Reader>(
        TRIG_PIN,
        ECHO_PIN,
        CONTAINER_DEPTH_CM,
        SENSOR_ID);

    auto gateway = std::make_unique<ConsoleGateway<SensorReading>>();

    auto feeder = std::make_unique<EspFeeder<SensorReading>>(
        std::move(reader),
        std::move(gateway));

    feeder->start();
    ESP_LOGI(TAG, "Feeder started; printing distance and fillLevel");

    while (true)
    {
        vTaskDelay(pdMS_TO_TICKS(10000));
    }
}
