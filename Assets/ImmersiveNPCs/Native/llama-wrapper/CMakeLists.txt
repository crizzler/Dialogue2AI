cmake_minimum_required(VERSION 3.21)
project(immersivenpcs_llama C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

find_package(Threads REQUIRED)

add_compile_definitions(GGML_VERSION=0x0 GGML_COMMIT=0x0)
set(GGML_VERSION_MAJOR 0)
set(GGML_VERSION_MINOR 0)
set(GGML_VERSION_PATCH 0)
set(GGML_VERSION "${GGML_VERSION_MAJOR}.${GGML_VERSION_MINOR}.${GGML_VERSION_PATCH}")

if (NOT OLLAMA_ROOT)
    set(OLLAMA_ROOT "${CMAKE_CURRENT_LIST_DIR}/../../../ollama-0.13.5")
endif()

get_filename_component(OLLAMA_ROOT "${OLLAMA_ROOT}" ABSOLUTE)
set(LLAMA_CPP_DIR "${OLLAMA_ROOT}/llama/llama.cpp")
set(GGML_DIR "${OLLAMA_ROOT}/ml/backend/ggml/ggml")

if (NOT EXISTS "${LLAMA_CPP_DIR}/include/llama.h")
    message(FATAL_ERROR "llama.cpp not found. Set OLLAMA_ROOT to the Ollama source root.")
endif()

if (NOT EXISTS "${GGML_DIR}/src/CMakeLists.txt")
    message(FATAL_ERROR "ggml not found. Expected ${GGML_DIR}/src/CMakeLists.txt")
endif()

# ggml configuration
set(GGML_BACKEND_DL OFF CACHE BOOL "" FORCE)
set(GGML_CPU ON CACHE BOOL "" FORCE)
set(GGML_OPENMP OFF CACHE BOOL "" FORCE)
set(GGML_SCHED_MAX_COPIES 4 CACHE STRING "" FORCE)
if (NOT DEFINED GGML_CUDA_PEER_MAX_BATCH_SIZE OR GGML_CUDA_PEER_MAX_BATCH_SIZE STREQUAL "")
    set(GGML_CUDA_PEER_MAX_BATCH_SIZE 128 CACHE STRING "" FORCE)
endif()
if (NOT DEFINED GGML_CUDA_COMPRESSION_MODE OR GGML_CUDA_COMPRESSION_MODE STREQUAL "")
    set(GGML_CUDA_COMPRESSION_MODE "speed" CACHE STRING "" FORCE)
endif()
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)

add_subdirectory("${GGML_DIR}/src" "${CMAKE_BINARY_DIR}/ggml")

file(GLOB LLAMA_SOURCES
    "${LLAMA_CPP_DIR}/src/*.cpp"
    "${LLAMA_CPP_DIR}/src/models/*.cpp"
)

add_library(immersivenpcs_llama SHARED
    immersivenpcs_llama.cpp
    ${LLAMA_SOURCES}
)

target_include_directories(immersivenpcs_llama PRIVATE
    "${LLAMA_CPP_DIR}/include"
    "${LLAMA_CPP_DIR}/src"
    "${LLAMA_CPP_DIR}/common"
    "${GGML_DIR}/include"
    "${GGML_DIR}/src"
    "${GGML_DIR}/src/include"
    "${GGML_DIR}/src/ggml-cpu"
    "${GGML_DIR}/src/ggml-cpu/amx"
)

target_link_libraries(immersivenpcs_llama PRIVATE ggml ggml-base)
if (TARGET ggml-cpu)
    target_link_libraries(immersivenpcs_llama PRIVATE ggml-cpu)
endif()

if (CMAKE_SYSTEM_NAME MATCHES "Linux")
    target_link_libraries(immersivenpcs_llama PRIVATE pthread dl)
endif()

if (IMNPC_OUTPUT_DIR)
    set_target_properties(immersivenpcs_llama PROPERTIES LIBRARY_OUTPUT_DIRECTORY "${IMNPC_OUTPUT_DIR}")
endif()

set_target_properties(immersivenpcs_llama PROPERTIES OUTPUT_NAME "immersivenpcs_llama")
