#include "pc_runner.h"

#include <iostream>
#include <string>

int main(int argc, char **argv)
{
    std::string broker_uri = "tcp://localhost:1883";
    std::string sensor_id = "1001";
    std::string provisioning_token = "pc-provision-token";
    int telemetry_interval_ms = 180000;
    size_t burst_sample_count = 5;

    if (argc > 1)
    {
        broker_uri = argv[1];
    }
    if (argc > 2)
    {
        sensor_id = argv[2];
    }
    if (argc > 3)
    {
        provisioning_token = argv[3];
    }

    std::cout << "[INFO] Starting PC telemetry runner with broker=" << broker_uri
              << " sensorId=" << sensor_id
              << " burstSamples=" << burst_sample_count
              << " intervalMs=" << telemetry_interval_ms << std::endl;

    PcRunner runner(
        broker_uri,
        sensor_id,
        provisioning_token,
        telemetry_interval_ms,
        burst_sample_count);

    return runner.run();
}
