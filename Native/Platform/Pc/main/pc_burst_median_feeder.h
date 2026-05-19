#pragma once

#include "DesktopFeeder.h"
#include "pc_types.h"

#include <algorithm>
#include <iostream>
#include <memory>
#include <optional>
#include <vector>

class BurstMedianServerFeeder : public ServerFeeder<SampleData>
{
    size_t burst_sample_count_;
    int telemetry_interval_ms_;

public:
    BurstMedianServerFeeder(
        std::unique_ptr<Reader<SampleData>> reader,
        std::unique_ptr<Gateway<SampleData>> gateway,
        size_t burst_sample_count,
        int telemetry_interval_ms)
        : ServerFeeder<SampleData>(std::move(reader), std::move(gateway)),
          burst_sample_count_(burst_sample_count),
          telemetry_interval_ms_(telemetry_interval_ms)
    {
    }

protected:
    std::optional<SampleData> read_cycle_data() override
    {
        if (burst_sample_count_ == 0)
        {
            return std::nullopt;
        }

        std::vector<int> distances;
        distances.reserve(burst_sample_count_);

        for (size_t i = 0; i < burst_sample_count_; ++i)
        {
            std::optional<SampleData> sample = this->reader().read();
            if (!sample.has_value())
            {
                std::cout << "[WARN] Burst acquisition failed at index " << i << std::endl;
                return std::nullopt;
            }

            distances.push_back(sample->distance_mm);
        }

        std::sort(distances.begin(), distances.end());
        int median_distance_mm = distances[distances.size() / 2];

        std::cout << "[INFO] Burst median distance: " << median_distance_mm
                  << " mm from " << burst_sample_count_ << " samples" << std::endl;

        return SampleData{
            median_distance_mm,
            static_cast<short>(burst_sample_count_)
        };
    }

    int cycle_sleep_ms() const override
    {
        return telemetry_interval_ms_;
    }
};
