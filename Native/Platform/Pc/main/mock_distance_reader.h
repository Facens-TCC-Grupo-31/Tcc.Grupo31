#pragma once

#include "Reader.h"
#include "pc_types.h"

#include <algorithm>
#include <optional>

class MockDistanceReader : public Reader<SampleData>
{
    int distance_mm_ = 0;

public:
    explicit MockDistanceReader(int desired_reading_mm)
    {
        distance_mm_ = std::max(1, desired_reading_mm);
    }

    std::optional<SampleData> read() override
    {
        SampleData data{ distance_mm_, 1 };

        return data;
    }
};
