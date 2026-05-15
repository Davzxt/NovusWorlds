#pragma once

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <string>

namespace Novus {

struct Vec3 {
  float x = 0.0f;
  float y = 0.0f;
  float z = 0.0f;
};

struct Color {
  float r = 1.0f;
  float g = 1.0f;
  float b = 1.0f;
  float a = 1.0f;
};

inline float DegToRad(float value) {
  return value * 3.1415926535f / 180.0f;
}

inline uint32_t PackColor(const Color& color) {
  const auto clamp = [](float v) -> uint32_t {
    return static_cast<uint32_t>(std::clamp(v, 0.0f, 1.0f) * 255.0f);
  };
  return (clamp(color.a) << 24) | (clamp(color.r) << 16) | (clamp(color.g) << 8) | clamp(color.b);
}

Color ColorFromHex(const std::string& hex, Color fallback = {});
std::string HexFromColor(const Color& color);

}
