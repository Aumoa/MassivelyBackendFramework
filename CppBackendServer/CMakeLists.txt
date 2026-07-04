cmake_minimum_required(VERSION 3.20)

project(CppBackendServer LANGUAGES CXX)

add_library(cpp_backend_packet_core STATIC
    src/packet_core.cpp)

target_include_directories(cpp_backend_packet_core
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include)

target_compile_features(cpp_backend_packet_core PUBLIC cxx_std_20)

if (MSVC)
    target_compile_options(cpp_backend_packet_core PRIVATE /W4 /permissive-)
else()
    target_compile_options(cpp_backend_packet_core PRIVATE -Wall -Wextra -Wpedantic)
endif()

if (WIN32)
    target_link_libraries(cpp_backend_packet_core PUBLIC ws2_32)
endif()

enable_testing()

add_executable(cpp_backend_packet_core_tests
    tests/packet_core_tests.cpp)

target_link_libraries(cpp_backend_packet_core_tests
    PRIVATE
        cpp_backend_packet_core)

add_test(NAME cpp_backend_packet_core_tests
    COMMAND cpp_backend_packet_core_tests)

add_executable(cpp_backend_sidecar_client_tests
    tests/sidecar_client_tests.cpp)

target_link_libraries(cpp_backend_sidecar_client_tests
    PRIVATE
        cpp_backend_packet_core)

add_test(NAME cpp_backend_sidecar_client_tests
    COMMAND cpp_backend_sidecar_client_tests)
