#include <stdio.h>
#include <memory>
#include <optional>
#include <atomic>
#include <cstring>

// Include abstractions from the Abstractions folder
// Adding the path to include abstractions
#include "../../../Abstractions/Reader.h"
#include "../../../Abstractions/Gateway.h"
#include "../../../Abstractions/Feeder.h"
#include "../../../Abstractions/EspFeeder.h"

#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "esp_log.h"

static const char *TAG = "Sampler";

/**
 * @brief Simple data structure to hold sampled data
 */
struct SampleData
{
    uint32_t timestamp;
    int32_t value;
};

/**
 * @brief Sampler implementation - reads sample data
 * Generates incremental values as a simple demo
 */
class SimpleSampler : public Reader<SampleData>
{
private:
    int counter = 0;

public:
    std::optional<SampleData> read() override
    {
        SampleData data;
        data.timestamp = xTaskGetTickCount();
        data.value = counter++;
        ESP_LOGI(TAG, "Sampled value: %ld at tick: %lu", data.value, data.timestamp);
        return data;
    }
};

/**
 * @brief Console Gateway implementation - prints data to console
 */
class ConsoleGateway : public Gateway<SampleData>
{
public:
    void send(const SampleData& data) override
    {
        ESP_LOGI(TAG, "Output - Value: %ld, Timestamp: %lu ms", 
                 data.value, 
                 data.timestamp * portTICK_PERIOD_MS);
    }
};

extern "C" void app_main(void)
{
    ESP_LOGI(TAG, "Starting Sampler Application...");
    
    // Create instances of Reader and Gateway
    auto sampler = std::make_unique<SimpleSampler>();
    auto console_gateway = std::make_unique<ConsoleGateway>();
    
    // Create EspFeeder with the sampler and gateway
    auto feeder = std::make_unique<EspFeeder<SampleData>>(
        std::move(sampler), 
        std::move(console_gateway)
    );
    
    // Start the feeder (runs async in a FreeRTOS task)
    feeder->start();
    ESP_LOGI(TAG, "Sampler started and running in background task");
    
    // Keep the main task alive
    while (1)
    {
        vTaskDelay(pdMS_TO_TICKS(5000));
        ESP_LOGI(TAG, "Main task - Sampler still running...");
    }
}
