cmake_minimum_required(VERSION 3.24)

project(NovusWorldsNative LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/$<CONFIG>")

add_library(novus_engine
  engine/src/Application.cpp
  engine/src/DataModel.cpp
  engine/src/HttpClient.cpp
  engine/src/Json.cpp
  engine/src/RendererD3D9.cpp
)

target_include_directories(novus_engine PUBLIC engine/include)
target_compile_definitions(novus_engine PUBLIC UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN)
target_link_libraries(novus_engine PUBLIC d3d9 winhttp user32 gdi32 shell32)

add_executable(NovusWorldsClient WIN32 client/main.cpp)
target_link_libraries(NovusWorldsClient PRIVATE novus_engine)

add_executable(NovusWorldsStudio WIN32 studio/main.cpp)
target_link_libraries(NovusWorldsStudio PRIVATE novus_engine)

add_executable(novus-nwm-convert tools/convert_nwm.cpp)
target_link_libraries(novus-nwm-convert PRIVATE novus_engine)

install(TARGETS NovusWorldsClient NovusWorldsStudio RUNTIME DESTINATION .)
