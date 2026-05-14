#pragma once

#include "Reader.h"
#include "ReadingDto.h"

#include <optional>
#include <string>

#include "driver/gpio.h"
#include "esp_check.h"
#include "esp_log.h"
#include "esp_rom_sys.h"
#include "esp_timer.h"

struct SensorReading
{
    float distance_cm;
    ReadingDto dto;
};

class HC_SR04_Reader : public Reader<SensorReading>
{
private:
    static constexpr const char *TAG = "HC_SR04_Reader";
    static constexpr int MAX_READ_ATTEMPTS = 3;
    static constexpr int64_t ECHO_WAIT_HIGH_TIMEOUT_US = 100000;
    static constexpr int64_t ECHO_WAIT_LOW_TIMEOUT_US = 60000;
    static constexpr int64_t ECHO_PRECONDITION_TIMEOUT_US = 5000;
    static constexpr uint32_t TRIG_PULSE_US = 20;
    static constexpr uint32_t POST_TRIG_SETTLE_US = 20;

    enum class ReadError
    {
        None,
        EchoStuckHighBeforeTrigger,
        EchoDidNotGoHigh,
        EchoDidNotGoLow,
    };

    gpio_num_t trig_pin_;
    gpio_num_t echo_pin_;
    float container_depth_cm_;
    std::string sensor_id_;

    bool read_distance_cm(float &distance_cm, ReadError &error, uint32_t &echo_edge_count)
    {
        error = ReadError::None;
        echo_edge_count = 0;

        // Ensure ECHO is low before triggering a new measurement.
        int64_t pre_t0 = esp_timer_get_time();
        while (gpio_get_level(echo_pin_) == 1)
        {
            if (esp_timer_get_time() - pre_t0 > ECHO_PRECONDITION_TIMEOUT_US)
            {
                error = ReadError::EchoStuckHighBeforeTrigger;
                return false;
            }
        }

        gpio_set_level(trig_pin_, 0);
        esp_rom_delay_us(2);
        gpio_set_level(trig_pin_, 1);
        esp_rom_delay_us(TRIG_PULSE_US);
        gpio_set_level(trig_pin_, 0);
        // Sensor needs a short processing time before ECHO can rise.
        esp_rom_delay_us(POST_TRIG_SETTLE_US);

        int64_t t0 = esp_timer_get_time();
        int last_level = gpio_get_level(echo_pin_);
        while (true)
        {
            const int current_level = gpio_get_level(echo_pin_);
            if (current_level != last_level)
            {
                ++echo_edge_count;
                last_level = current_level;
            }

            if (current_level == 1)
            {
                break;
            }

            if (esp_timer_get_time() - t0 > ECHO_WAIT_HIGH_TIMEOUT_US)
            {
                error = ReadError::EchoDidNotGoHigh;
                return false;
            }
        }

        int64_t start = esp_timer_get_time();
        while (gpio_get_level(echo_pin_) == 1)
        {
            if (esp_timer_get_time() - start > ECHO_WAIT_LOW_TIMEOUT_US)
            {
                error = ReadError::EchoDidNotGoLow;
                return false;
            }
        }

        const int64_t end = esp_timer_get_time();
        const int64_t pulse_width_us = end - start;
        distance_cm = static_cast<float>(pulse_width_us) / 58.0f;
        return true;
    }

public:
    HC_SR04_Reader(gpio_num_t trig_pin, gpio_num_t echo_pin, float container_depth_cm, std::string sensor_id)
        : trig_pin_(trig_pin),
          echo_pin_(echo_pin),
          container_depth_cm_(container_depth_cm),
          sensor_id_(std::move(sensor_id))
    {
        gpio_config_t trig_cfg = {};
        trig_cfg.pin_bit_mask = (1ULL << trig_pin_);
        trig_cfg.mode = GPIO_MODE_OUTPUT;
        trig_cfg.pull_down_en = GPIO_PULLDOWN_DISABLE;
        trig_cfg.pull_up_en = GPIO_PULLUP_DISABLE;
        trig_cfg.intr_type = GPIO_INTR_DISABLE;
        ESP_ERROR_CHECK(gpio_config(&trig_cfg));

        gpio_config_t echo_cfg = {};
        echo_cfg.pin_bit_mask = (1ULL << echo_pin_);
        echo_cfg.mode = GPIO_MODE_INPUT;
        echo_cfg.pull_down_en = GPIO_PULLDOWN_DISABLE;
        echo_cfg.pull_up_en = GPIO_PULLUP_DISABLE;
        echo_cfg.intr_type = GPIO_INTR_DISABLE;
        ESP_ERROR_CHECK(gpio_config(&echo_cfg));

        // Sensor startup settling time.
        esp_rom_delay_us(50000);

        ESP_LOGI(TAG,
                 "Initialized HC-SR04 (TRIG=%d ECHO=%d depth=%.2fcm trigPulse=%uus)",
                 static_cast<int>(trig_pin_),
                 static_cast<int>(echo_pin_),
                 container_depth_cm_,
                 TRIG_PULSE_US);
    }

    std::optional<SensorReading> read() override
    {
        float distance_cm = 0.0f;
        ReadError error = ReadError::None;
        uint32_t echo_edge_count = 0;
        bool success = false;
        for (int attempt = 1; attempt <= MAX_READ_ATTEMPTS; ++attempt)
        {
            if (read_distance_cm(distance_cm, error, echo_edge_count))
            {
                success = true;
                break;
            }
            esp_rom_delay_us(2000);
        }

        if (!success)
        {
            const int echo_level = gpio_get_level(echo_pin_);
            switch (error)
            {
            case ReadError::EchoStuckHighBeforeTrigger:
                ESP_LOGW(TAG, "Timeout: ECHO stuck HIGH before trigger after %d attempts (echo_level=%d edges=%lu)", MAX_READ_ATTEMPTS, echo_level, static_cast<unsigned long>(echo_edge_count));
                break;
            case ReadError::EchoDidNotGoHigh:
                ESP_LOGW(TAG, "Timeout: ECHO never went HIGH after trigger after %d attempts (echo_level=%d edges=%lu)", MAX_READ_ATTEMPTS, echo_level, static_cast<unsigned long>(echo_edge_count));
                break;
            case ReadError::EchoDidNotGoLow:
                ESP_LOGW(TAG, "Timeout: ECHO stayed HIGH too long after %d attempts (echo_level=%d edges=%lu)", MAX_READ_ATTEMPTS, echo_level, static_cast<unsigned long>(echo_edge_count));
                break;
            default:
                ESP_LOGW(TAG, "Timeout: unknown read error after %d attempts (echo_level=%d edges=%lu)", MAX_READ_ATTEMPTS, echo_level, static_cast<unsigned long>(echo_edge_count));
                break;
            }
            return std::nullopt;
        }

        float fill_level = 1.0f - (distance_cm / container_depth_cm_);
        if (fill_level < 0.0f)
        {
            fill_level = 0.0f;
        }
        if (fill_level > 1.0f)
        {
            fill_level = 1.0f;
        }

        SensorReading output = {
            .distance_cm = distance_cm,
            .dto = {
                .sensorId = sensor_id_,
                .fillLevel = fill_level,
            },
        };

        return output;
    }
};
