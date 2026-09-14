cmake_minimum_required(VERSION 3.16)
project(nuvexadb VERSION 1.0.2 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(nuvexadb INTERFACE)
target_include_directories(nuvexadb INTERFACE "${CMAKE_CURRENT_SOURCE_DIR}/include")

if(DEFINED ENV{NUVEXA_NATIVE_DIR} AND NOT "$ENV{NUVEXA_NATIVE_DIR}" STREQUAL "")
  set(NUVEXA_NATIVE_DIR "$ENV{NUVEXA_NATIVE_DIR}")
else()
  set(NUVEXA_NATIVE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../artifacts/native/osx-arm64")
endif()

function(nuvexa_link target)
  target_link_libraries(${target} PRIVATE nuvexadb)
  target_link_directories(${target} PRIVATE "${NUVEXA_NATIVE_DIR}")
  target_link_libraries(${target} PRIVATE nuvexa)
  set_target_properties(${target} PROPERTIES
    BUILD_RPATH "${NUVEXA_NATIVE_DIR}"
    INSTALL_RPATH "${NUVEXA_NATIVE_DIR}")
endfunction()

add_executable(nuvexa_tests tests/interop.cpp)
nuvexa_link(nuvexa_tests)

add_executable(nuvexa_sample examples/sample.cpp)
nuvexa_link(nuvexa_sample)

enable_testing()
add_test(NAME interop COMMAND nuvexa_tests)
