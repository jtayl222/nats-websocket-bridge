cmake_minimum_required(VERSION 3.14)
project(packaging_line_demo LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Add the SDK
add_subdirectory(${CMAKE_CURRENT_SOURCE_DIR}/../../sdk/cpp ${CMAKE_BINARY_DIR}/sdk)

# Common utilities
add_library(demo_common STATIC
    common/demo_utils.cpp
    common/demo_utils.h
)
target_include_directories(demo_common PUBLIC common)
target_link_libraries(demo_common PUBLIC gateway_device)

# Device simulators
add_executable(temperature_sensor temperature_sensor.cpp)
target_link_libraries(temperature_sensor PRIVATE demo_common)

add_executable(conveyor_controller conveyor_controller.cpp)
target_link_libraries(conveyor_controller PRIVATE demo_common)

add_executable(vision_scanner vision_scanner.cpp)
target_link_libraries(vision_scanner PRIVATE demo_common)

add_executable(estop_button estop_button.cpp)
target_link_libraries(estop_button PRIVATE demo_common)

add_executable(production_counter production_counter.cpp)
target_link_libraries(production_counter PRIVATE demo_common)

# Orchestrator
add_executable(line_orchestrator ../orchestrator/line_orchestrator.cpp)
target_link_libraries(line_orchestrator PRIVATE demo_common)

# HMI
add_executable(hmi_panel ../hmi/hmi_panel.cpp)
target_link_libraries(hmi_panel PRIVATE demo_common)
