#pragma once

#include "Gateway.h"
#include "pc_types.h"

#include <mqtt/async_client.h>

#include <iostream>
#include <string>

class PcMqttGateway : public Gateway<SampleData>
{
    static constexpr const char *TELEMETRY_TOPIC = "devices/mock/samples";

    mqtt::async_client client_;
    std::string sensor_id_;

public:
    PcMqttGateway(
        const std::string &broker_uri,
        const std::string &client_id,
        const std::string &sensor_id)
        : client_(broker_uri, client_id),
          sensor_id_(sensor_id)
    {
        mqtt::connect_options opts;
        opts.set_automatic_reconnect(true);
        opts.set_clean_session(true);
        client_.connect(opts)->wait();

        std::cout << "[INFO] Connected to MQTT broker: " << broker_uri << std::endl;
    }

    ~PcMqttGateway() override
    {
        try
        {
            if (client_.is_connected())
            {
                client_.disconnect()->wait();
            }
        }
        catch (...)
        {
        }
    }

    void send(const SampleData &data) override
    {
        std::string telemetry_payload =
            "{\"sensorId\":" + sensor_id_ +
            ",\"distanceMm\":" + std::to_string(data.distance_mm) +
            "}";

        client_.publish(TELEMETRY_TOPIC, telemetry_payload.c_str(), static_cast<int>(telemetry_payload.size()), 1, false)->wait();
        std::cout << "[INFO] Published telemetry payload: " << telemetry_payload << std::endl;
    }
};
