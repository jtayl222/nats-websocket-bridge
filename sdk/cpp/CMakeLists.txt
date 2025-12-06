cmake_minimum_required(VERSION 3.14)
project(gateway_device_sdk VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

# Options
option(BUILD_EXAMPLES "Build example applications" ON)
option(BUILD_TESTS "Build unit tests" OFF)
option(BUILD_SHARED_LIBS "Build shared library" OFF)

# Find dependencies
find_package(Threads REQUIRED)
find_package(OpenSSL REQUIRED)

# Try to find libwebsockets (optional - will use bundled if not found)
find_package(PkgConfig QUIET)
if(PkgConfig_FOUND)
    pkg_check_modules(LIBWEBSOCKETS libwebsockets)
endif()

# Try to find nlohmann_json
find_package(nlohmann_json QUIET)

# If nlohmann_json not found, we'll use header-only version
if(NOT nlohmann_json_FOUND)
    include(FetchContent)
    FetchContent_Declare(
        json
        URL https://github.com/nlohmann/json/releases/download/v3.11.3/json.tar.xz
    )
    FetchContent_MakeAvailable(json)
endif()

# If libwebsockets not found, fetch it
if(NOT LIBWEBSOCKETS_FOUND)
    include(FetchContent)
    FetchContent_Declare(
        libwebsockets
        GIT_REPOSITORY https://github.com/warmcat/libwebsockets.git
        GIT_TAG v4.3.3
    )
    set(LWS_WITH_SSL ON CACHE BOOL "" FORCE)
    set(LWS_WITHOUT_TESTAPPS ON CACHE BOOL "" FORCE)
    set(LWS_WITHOUT_TEST_SERVER ON CACHE BOOL "" FORCE)
    set(LWS_WITHOUT_TEST_PING ON CACHE BOOL "" FORCE)
    set(LWS_WITHOUT_TEST_CLIENT ON CACHE BOOL "" FORCE)
    FetchContent_MakeAvailable(libwebsockets)
endif()

# SDK Library
set(SDK_SOURCES
    src/gateway_client.cpp
    src/websocket_transport.cpp
    src/protocol.cpp
    src/message.cpp
    src/auth.cpp
    src/reconnect_policy.cpp
    src/logger.cpp
)

set(SDK_HEADERS
    include/gateway/gateway_device.h
    include/gateway/types.h
    include/gateway/config.h
    include/gateway/message.h
    include/gateway/protocol.h
    include/gateway/transport.h
    include/gateway/auth.h
    include/gateway/error.h
    include/gateway/logger.h
    include/gateway/reconnect_policy.h
)

add_library(gateway_device ${SDK_SOURCES} ${SDK_HEADERS})

target_include_directories(gateway_device
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
)

target_link_libraries(gateway_device
    PUBLIC
        Threads::Threads
    PRIVATE
        OpenSSL::SSL
        OpenSSL::Crypto
        nlohmann_json::nlohmann_json
)

if(LIBWEBSOCKETS_FOUND)
    target_include_directories(gateway_device PRIVATE ${LIBWEBSOCKETS_INCLUDE_DIRS})
    target_link_libraries(gateway_device PRIVATE ${LIBWEBSOCKETS_LIBRARIES})
else()
    target_link_libraries(gateway_device PRIVATE websockets)
endif()

# Set library version
set_target_properties(gateway_device PROPERTIES
    VERSION ${PROJECT_VERSION}
    SOVERSION 1
)

# Examples
if(BUILD_EXAMPLES)
    add_subdirectory(examples)
endif()

# Tests
if(BUILD_TESTS)
    enable_testing()
    add_subdirectory(tests)
endif()

# Installation
include(GNUInstallDirs)

install(TARGETS gateway_device
    EXPORT gateway_device_targets
    LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
    ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
)

install(DIRECTORY include/gateway
    DESTINATION ${CMAKE_INSTALL_INCLUDEDIR}
)

install(EXPORT gateway_device_targets
    FILE gateway_device_targets.cmake
    NAMESPACE GatewayDevice::
    DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/gateway_device
)

# Package config
include(CMakePackageConfigHelpers)

configure_package_config_file(
    ${CMAKE_CURRENT_SOURCE_DIR}/cmake/gateway_device_config.cmake.in
    ${CMAKE_CURRENT_BINARY_DIR}/gateway_device_config.cmake
    INSTALL_DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/gateway_device
)

write_basic_package_version_file(
    ${CMAKE_CURRENT_BINARY_DIR}/gateway_device_config_version.cmake
    VERSION ${PROJECT_VERSION}
    COMPATIBILITY SameMajorVersion
)

install(FILES
    ${CMAKE_CURRENT_BINARY_DIR}/gateway_device_config.cmake
    ${CMAKE_CURRENT_BINARY_DIR}/gateway_device_config_version.cmake
    DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/gateway_device
)
