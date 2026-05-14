#pragma once

#include "Gateway.h"
#include "esp_log.h"

#include <cstdio>

template <typename TData>
class ConsoleGateway : public Gateway<TData>
{
private:
    static constexpr const char *TAG = "ConsoleGateway";

public:
    void send(const TData &data) override
    {
        printf("[READING] sensor=%s distance=%.2fcm fillLevel=%.3f\n",
               data.dto.sensorId.c_str(),
               data.distance_cm,
               data.dto.fillLevel);
        ESP_LOGI(TAG,
                 "sensor=%s distance=%.2fcm fillLevel=%.3f",
                 data.dto.sensorId.c_str(),
                 data.distance_cm,
                 data.dto.fillLevel);
    }
};
