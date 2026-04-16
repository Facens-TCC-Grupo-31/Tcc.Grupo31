#pragma once

#include <string>

struct Reading
{
    std::string sensorId;
    long timestamp;
    float fillLevel;
};
