#pragma once

#include "Reader.h"
#include "pc_types.h"

#include <optional>

class MockDistanceReader : public Reader<SampleData>
{
    int distance_mm_ = 200;
    bool increasing_ = true;

public:
    std::optional<SampleData> read() override
    {
        SampleData data{ .distance_mm = distance_mm_, .burst_sample_count = 1 };

        if (increasing_)
        {
            distance_mm_ += 3;
            if (distance_mm_ >= 260)
            {
                distance_mm_ = 260;
                increasing_ = false;
            }
        }
        else
        {
            distance_mm_ -= 3;
            if (distance_mm_ <= 140)
            {
                distance_mm_ = 140;
                increasing_ = true;
            }
        }

        return data;
    }
};
