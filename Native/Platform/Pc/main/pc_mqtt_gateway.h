#pragma once

#include "Gateway.h"
#include "pc_types.h"

#include <mqtt/async_client.h>

#include <iostream>
#include <string>

class PcMqttGateway : public Gateway<SampleData>
{
    static constexpr const char *REGISTRATION_TOPIC = "devices/register";
    static constexpr const char *TELEMETRY_TOPIC = "devices/samples";

    mqtt::async_client client_;
    std::string sensor_id_;
    std::string provisioning_token_;
    bool has_registration_token_;

public:
    PcMqttGateway(
        const std::string &broker_uri,
        const std::string &client_id,
        const std::string &sensor_id,
        const std::string &provisioning_token)
        : client_(broker_uri, client_id),
          sensor_id_(sensor_id),
          provisioning_token_(provisioning_token),
          has_registration_token_(!provisioning_token.empty())
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
        if (has_registration_token_)
        {
            std::string registration_payload =
                "{\"sensorId\":" + sensor_id_ +
                ",\"provisioningToken\":\"" + provisioning_token_ +
                "\",\"baselineDistanceMm\":" + std::to_string(data.distance_mm) +
                ",\"calibrationSampleCount\":" + std::to_string(data.burst_sample_count) +
                "}";

            client_.publish(REGISTRATION_TOPIC, registration_payload.c_str(), static_cast<int>(registration_payload.size()), 1, false)->wait();
            has_registration_token_ = false;

            std::cout << "[INFO] Published registration payload: " << registration_payload << std::endl;
            return;
        }

        std::string telemetry_payload =
            "{\"sensorId\":" + sensor_id_ +
            ",\"distanceMm\":" + std::to_string(data.distance_mm) +
            "}";

        client_.publish(TELEMETRY_TOPIC, telemetry_payload.c_str(), static_cast<int>(telemetry_payload.size()), 1, false)->wait();
        std::cout << "[INFO] Published telemetry payload: " << telemetry_payload << std::endl;
    }
};
