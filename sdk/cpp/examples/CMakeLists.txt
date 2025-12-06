# Examples CMakeLists.txt

# Simple sensor example
add_executable(simple_sensor simple_sensor.cpp)
target_link_libraries(simple_sensor PRIVATE gateway_device)

# Temperature sensor example
add_executable(temperature_sensor temperature_sensor.cpp)
target_link_libraries(temperature_sensor PRIVATE gateway_device)

# Actuator example
add_executable(actuator actuator.cpp)
target_link_libraries(actuator PRIVATE gateway_device)

# Controller example
add_executable(controller controller.cpp)
target_link_libraries(controller PRIVATE gateway_device)
