#include "pc_runner.h"

#include <algorithm>
#include <csignal>
#include <iostream>
#include <string>

std::atomic<bool> PcRunner::shutdown_requested_ = false;
std::mutex PcRunner::shutdown_mutex_;
std::condition_variable PcRunner::shutdown_cv_;

int main(int argc, char **argv)
{
    if (argc < 3)
    {
        std::cerr << "Usage: pc_runner <sensorId> <desiredReadingMm> [brokerUri]" << std::endl;
        return 1;
    }

    std::string sensor_id = argv[1];
    int desired_reading_mm = std::max(1, std::stoi(argv[2]));

    std::string broker_uri = "tcp://localhost:1883";
    int telemetry_interval_ms = 180000;
    size_t burst_sample_count = 5;

    if (argc > 3)
    {
        broker_uri = argv[3];
    }

    std::signal(SIGTERM, PcRunner::handle_signal);
    std::signal(SIGINT,  PcRunner::handle_signal);

    std::cout << "[INFO] Starting PC telemetry runner with broker=" << broker_uri
              << " sensorId=" << sensor_id
              << " desiredReadingMm=" << desired_reading_mm
              << " burstSamples=" << burst_sample_count
              << " intervalMs=" << telemetry_interval_ms << std::endl;

    PcRunner runner(
        broker_uri,
        sensor_id,
        desired_reading_mm,
        telemetry_interval_ms,
        burst_sample_count);

    return runner.run();
}
