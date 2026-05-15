#pragma once

#include "Novus/DataModel.h"

#include <d3d9.h>
#include <string>
#include <windows.h>

namespace Novus {

struct Camera {
  Vec3 target = { 0.0f, 2.0f, 0.0f };
  float yaw = 0.65f;
  float pitch = 0.42f;
  float distance = 42.0f;
};

class RendererD3D9 {
public:
  RendererD3D9() = default;
  ~RendererD3D9();

  bool Initialize(HWND hwnd);
  void Shutdown();
  void Resize(int width, int height);
  void BeginFrame(Color sky);
  void RenderDataModel(const DataModel& model, const Camera& camera);
  void RenderClassicR6(const Vec3& position, float yaw, float animationClock, const std::string& animation);
  void EndFrame();

  int Width() const { return width_; }
  int Height() const { return height_; }
  IDirect3DDevice9* Device() const { return device_; }

private:
  void ResetDevice();
  void ApplyCamera(const Camera& camera);
  void DrawPart(const Instance& part);
  void DrawBox(const Vec3& position, const Vec3& rotation, const Vec3& size, const Color& color);
  void DrawStuds(const Instance& part);

  HWND hwnd_ = nullptr;
  IDirect3D9* d3d_ = nullptr;
  IDirect3DDevice9* device_ = nullptr;
  D3DPRESENT_PARAMETERS params_{};
  int width_ = 1280;
  int height_ = 720;
  bool sceneOpen_ = false;
};

}
