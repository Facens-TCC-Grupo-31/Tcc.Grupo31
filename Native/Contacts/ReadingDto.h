#pragma once

#include <string>

struct ReadingDto
{
    std::string sensorId;
    long timestamp;
    float fillLevel;
};
