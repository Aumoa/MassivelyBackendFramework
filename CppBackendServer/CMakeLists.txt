cmake_minimum_required(VERSION 3.20)

project(CppBackendServer LANGUAGES CXX)

set_property(GLOBAL PROPERTY USE_FOLDERS ON)

set(CPP_BACKEND_PACKET_CORE_SOURCES
    include/cpp_backend/packet_core.hpp
    src/packet_core.cpp)

add_library(cpp_backend_packet_core STATIC
    ${CPP_BACKEND_PACKET_CORE_SOURCES})

target_include_directories(cpp_backend_packet_core
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include)

target_compile_features(cpp_backend_packet_core
    PUBLIC
        cxx_std_20)

set_target_properties(cpp_backend_packet_core
    PROPERTIES
        CXX_EXTENSIONS OFF
        FOLDER "CppBackendServer")

source_group(TREE ${CMAKE_CURRENT_SOURCE_DIR}
    FILES
        ${CPP_BACKEND_PACKET_CORE_SOURCES})

if(MSVC)
    target_compile_options(cpp_backend_packet_core
        PRIVATE
            /W4
            /permissive-)
else()
    target_compile_options(cpp_backend_packet_core
        PRIVATE
            -Wall
            -Wextra
            -Wpedantic)
endif()

if(WIN32)
    target_link_libraries(cpp_backend_packet_core
        PUBLIC
            ws2_32)
endif()

include(CTest)

function(cpp_backend_add_test target_name source_file)
    add_executable(${target_name}
        ${source_file})

    target_link_libraries(${target_name}
        PRIVATE
            cpp_backend_packet_core)

    set_target_properties(${target_name}
        PROPERTIES
            FOLDER "CppBackendServer/Tests")

    source_group(TREE ${CMAKE_CURRENT_SOURCE_DIR}
        FILES
            ${source_file})

    add_test(NAME ${target_name}
        COMMAND ${target_name})
endfunction()

if(BUILD_TESTING)
    cpp_backend_add_test(cpp_backend_packet_core_tests
        tests/packet_core_tests.cpp)

    cpp_backend_add_test(cpp_backend_sidecar_client_tests
        tests/sidecar_client_tests.cpp)
endif()
