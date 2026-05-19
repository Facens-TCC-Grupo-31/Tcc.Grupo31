#pragma once

#include "mock_distance_reader.h"
#include "pc_burst_median_feeder.h"
#include "pc_mqtt_gateway.h"

#include <memory>
#include <string>

class PcRunner
{
    std::string broker_uri_;
    std::string sensor_id_;
    int desired_reading_mm_;
    int telemetry_interval_ms_;
    size_t burst_sample_count_;

public:
    PcRunner(
        std::string broker_uri,
        std::string sensor_id,
        int desired_reading_mm,
        int telemetry_interval_ms,
        size_t burst_sample_count)
        : broker_uri_(std::move(broker_uri)),
          sensor_id_(std::move(sensor_id)),
          desired_reading_mm_(desired_reading_mm),
          telemetry_interval_ms_(telemetry_interval_ms),
          burst_sample_count_(burst_sample_count)
    {
    }

    int run()
    {
        auto reader = std::make_unique<MockDistanceReader>(desired_reading_mm_);
        auto gateway = std::make_unique<PcMqttGateway>(
            broker_uri_,
            "pc-runner-" + sensor_id_,
            sensor_id_);

        auto feeder = std::make_unique<BurstMedianServerFeeder>(
            std::move(reader),
            std::move(gateway),
            burst_sample_count_,
            telemetry_interval_ms_);

        feeder->start();

        std::cout << "[INFO] PcRunner started. Press Enter to stop..." << std::endl;
        std::string ignored;
        std::getline(std::cin, ignored);

        feeder->stop();
        return 0;
    }
};
