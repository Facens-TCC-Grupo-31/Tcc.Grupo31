#include <memory>
#include <vector>
#include <algorithm>
#include <optional>

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
static constexpr size_t BURST_SAMPLE_COUNT = 5;
static constexpr int BURST_INTER_SAMPLE_DELAY_MS = 70;

class BurstMedianEspFeeder : public EspFeeder<SensorReading>
{
    size_t burst_sample_count_;
    int inter_sample_delay_ms_;

public:
    BurstMedianEspFeeder(
        std::unique_ptr<Reader<SensorReading>> reader,
        std::unique_ptr<Gateway<SensorReading>> gateway,
        size_t burst_sample_count,
        int inter_sample_delay_ms)
        : EspFeeder<SensorReading>(std::move(reader), std::move(gateway)),
          burst_sample_count_(burst_sample_count),
          inter_sample_delay_ms_(inter_sample_delay_ms)
    {
    }

protected:
    std::optional<SensorReading> read_cycle_data() override
    {
        if (burst_sample_count_ == 0)
        {
            return std::nullopt;
        }

        std::vector<SensorReading> samples;
        samples.reserve(burst_sample_count_);

        for (size_t i = 0; i < burst_sample_count_; ++i)
        {
            std::optional<SensorReading> sample = reader().read();
            if (!sample.has_value())
            {
                ESP_LOGW(TAG, "Burst acquisition failed at index %d", static_cast<int>(i));
                return std::nullopt;
            }

            samples.push_back(sample.value());

            if ((i + 1) < burst_sample_count_)
            {
                vTaskDelay(pdMS_TO_TICKS(inter_sample_delay_ms_));
            }
        }

        std::sort(
            samples.begin(),
            samples.end(),
            [](const SensorReading &left, const SensorReading &right)
            {
                return left.distance_cm < right.distance_cm;
            });

        const SensorReading median_sample = samples[samples.size() / 2];
        ESP_LOGI(TAG,
                 "Burst median distance: %.2f cm from %d samples",
                 median_sample.distance_cm,
                 static_cast<int>(burst_sample_count_));

        return median_sample;
    }
};

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

    auto feeder = std::make_unique<BurstMedianEspFeeder>(
        std::move(reader),
        std::move(gateway),
        BURST_SAMPLE_COUNT,
        BURST_INTER_SAMPLE_DELAY_MS);

    feeder->start();
    ESP_LOGI(TAG, "Feeder started; printing distance and fillLevel");

    while (true)
    {
        vTaskDelay(pdMS_TO_TICKS(10000));
    }
}
