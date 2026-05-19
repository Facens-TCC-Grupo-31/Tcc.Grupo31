cmake_minimum_required(VERSION 3.22)
project(PcTelemetryRunner LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

find_package(PahoMqttCpp REQUIRED)

add_executable(pc_telemetry_runner
    main.cpp
)

target_include_directories(pc_telemetry_runner PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/main
    ${CMAKE_CURRENT_SOURCE_DIR}/../../Abstractions
)

target_link_libraries(pc_telemetry_runner PRIVATE
    PahoMqttCpp::paho-mqttpp3
)
