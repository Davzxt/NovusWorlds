#include "Novus/RendererD3D9.h"

#include <algorithm>
#include <cmath>
#include <functional>
#include <vector>

namespace Novus {
namespace {

struct Vertex {
  float x, y, z;
  DWORD color;
  enum { FVF = D3DFVF_XYZ | D3DFVF_DIFFUSE };
};

D3DMATRIX Identity() {
  D3DMATRIX m{};
  m._11 = m._22 = m._33 = m._44 = 1.0f;
  return m;
}

D3DMATRIX Multiply(const D3DMATRIX& a, const D3DMATRIX& b) {
  D3DMATRIX r{};
  const float* av = &a._11;
  const float* bv = &b._11;
  float* rv = &r._11;
  for (int row = 0; row < 4; ++row) {
    for (int col = 0; col < 4; ++col) {
      rv[row * 4 + col] =
        av[row * 4 + 0] * bv[0 * 4 + col] +
        av[row * 4 + 1] * bv[1 * 4 + col] +
        av[row * 4 + 2] * bv[2 * 4 + col] +
        av[row * 4 + 3] * bv[3 * 4 + col];
    }
  }
  return r;
}

D3DMATRIX Translation(float x, float y, float z) {
  D3DMATRIX m = Identity();
  m._41 = x;
  m._42 = y;
  m._43 = z;
  return m;
}

D3DMATRIX Scale(float x, float y, float z) {
  D3DMATRIX m = Identity();
  m._11 = x;
  m._22 = y;
  m._33 = z;
  return m;
}

D3DMATRIX RotationX(float r) {
  D3DMATRIX m = Identity();
  const float c = std::cos(r);
  const float s = std::sin(r);
  m._22 = c;
  m._23 = s;
  m._32 = -s;
  m._33 = c;
  return m;
}

D3DMATRIX RotationY(float r) {
  D3DMATRIX m = Identity();
  const float c = std::cos(r);
  const float s = std::sin(r);
  m._11 = c;
  m._13 = -s;
  m._31 = s;
  m._33 = c;
  return m;
}

D3DMATRIX RotationZ(float r) {
  D3DMATRIX m = Identity();
  const float c = std::cos(r);
  const float s = std::sin(r);
  m._11 = c;
  m._12 = s;
  m._21 = -s;
  m._22 = c;
  return m;
}

D3DMATRIX Perspective(float fovy, float aspect, float zn, float zf) {
  const float y = 1.0f / std::tan(fovy * 0.5f);
  const float x = y / aspect;
  D3DMATRIX m{};
  m._11 = x;
  m._22 = y;
  m._33 = zf / (zf - zn);
  m._34 = 1.0f;
  m._43 = (-zn * zf) / (zf - zn);
  return m;
}

Vec3 Sub(const Vec3& a, const Vec3& b) { return { a.x - b.x, a.y - b.y, a.z - b.z }; }
Vec3 Cross(const Vec3& a, const Vec3& b) { return { a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x }; }
float Dot(const Vec3& a, const Vec3& b) { return a.x * b.x + a.y * b.y + a.z * b.z; }

Vec3 Normalize(const Vec3& v) {
  const float len = std::sqrt(Dot(v, v));
  if (len <= 0.0001f) return {};
  return { v.x / len, v.y / len, v.z / len };
}

D3DMATRIX LookAt(const Vec3& eye, const Vec3& at, const Vec3& up) {
  const Vec3 z = Normalize(Sub(at, eye));
  const Vec3 x = Normalize(Cross(up, z));
  const Vec3 y = Cross(z, x);
  D3DMATRIX m = Identity();
  m._11 = x.x; m._21 = x.y; m._31 = x.z; m._41 = -Dot(x, eye);
  m._12 = y.x; m._22 = y.y; m._32 = y.z; m._42 = -Dot(y, eye);
  m._13 = z.x; m._23 = z.y; m._33 = z.z; m._43 = -Dot(z, eye);
  return m;
}

std::vector<Vertex> CubeVertices(DWORD color) {
  const float p = 0.5f;
  const Vec3 v[8] = {
    {-p,-p,-p},{-p, p,-p},{ p, p,-p},{ p,-p,-p},
    {-p,-p, p},{-p, p, p},{ p, p, p},{ p,-p, p}
  };
  const int idx[36] = {
    0,1,2, 0,2,3, 7,6,5, 7,5,4,
    4,5,1, 4,1,0, 3,2,6, 3,6,7,
    1,5,6, 1,6,2, 4,0,3, 4,3,7
  };
  std::vector<Vertex> out;
  out.reserve(36);
  for (int i : idx) out.push_back({ v[i].x, v[i].y, v[i].z, color });
  return out;
}

}

RendererD3D9::~RendererD3D9() {
  Shutdown();
}

bool RendererD3D9::Initialize(HWND hwnd) {
  hwnd_ = hwnd;
  RECT rc{};
  GetClientRect(hwnd_, &rc);
  width_ = std::max(1L, rc.right - rc.left);
  height_ = std::max(1L, rc.bottom - rc.top);

  d3d_ = Direct3DCreate9(D3D_SDK_VERSION);
  if (!d3d_) return false;

  params_ = {};
  params_.Windowed = TRUE;
  params_.SwapEffect = D3DSWAPEFFECT_DISCARD;
  params_.BackBufferFormat = D3DFMT_UNKNOWN;
  params_.EnableAutoDepthStencil = TRUE;
  params_.AutoDepthStencilFormat = D3DFMT_D24S8;
  params_.PresentationInterval = D3DPRESENT_INTERVAL_IMMEDIATE;
  params_.BackBufferWidth = width_;
  params_.BackBufferHeight = height_;

  HRESULT hr = d3d_->CreateDevice(
    D3DADAPTER_DEFAULT,
    D3DDEVTYPE_HAL,
    hwnd_,
    D3DCREATE_HARDWARE_VERTEXPROCESSING,
    &params_,
    &device_);
  if (FAILED(hr)) {
    hr = d3d_->CreateDevice(D3DADAPTER_DEFAULT, D3DDEVTYPE_HAL, hwnd_, D3DCREATE_SOFTWARE_VERTEXPROCESSING, &params_, &device_);
  }
  if (FAILED(hr)) return false;

  device_->SetRenderState(D3DRS_ZENABLE, TRUE);
  device_->SetRenderState(D3DRS_LIGHTING, FALSE);
  device_->SetRenderState(D3DRS_CULLMODE, D3DCULL_CCW);
  device_->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
  device_->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_SRCALPHA);
  device_->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_INVSRCALPHA);
  return true;
}

void RendererD3D9::Shutdown() {
  if (device_) {
    device_->Release();
    device_ = nullptr;
  }
  if (d3d_) {
    d3d_->Release();
    d3d_ = nullptr;
  }
}

void RendererD3D9::Resize(int width, int height) {
  width_ = std::max(1, width);
  height_ = std::max(1, height);
  ResetDevice();
}

void RendererD3D9::ResetDevice() {
  if (!device_) return;
  params_.BackBufferWidth = width_;
  params_.BackBufferHeight = height_;
  device_->Reset(&params_);
}

void RendererD3D9::BeginFrame(Color sky) {
  if (!device_) return;
  device_->Clear(0, nullptr, D3DCLEAR_TARGET | D3DCLEAR_ZBUFFER, PackColor(sky), 1.0f, 0);
  if (SUCCEEDED(device_->BeginScene())) sceneOpen_ = true;
}

void RendererD3D9::ApplyCamera(const Camera& camera) {
  const float cp = std::cos(camera.pitch);
  const Vec3 eye = {
    camera.target.x - std::sin(camera.yaw) * cp * camera.distance,
    camera.target.y + std::sin(camera.pitch) * camera.distance,
    camera.target.z - std::cos(camera.yaw) * cp * camera.distance
  };
  const D3DMATRIX view = LookAt(eye, camera.target, { 0.0f, 1.0f, 0.0f });
  const D3DMATRIX projection = Perspective(DegToRad(60.0f), static_cast<float>(width_) / static_cast<float>(height_), 0.1f, 2000.0f);
  device_->SetTransform(D3DTS_VIEW, &view);
  device_->SetTransform(D3DTS_PROJECTION, &projection);
}

void RendererD3D9::RenderDataModel(const DataModel& model, const Camera& camera) {
  if (!device_ || !sceneOpen_) return;
  ApplyCamera(camera);
  std::function<void(const Instance&)> draw = [&](const Instance& instance) {
    if (!instance.visible) return;
    if (instance.type == InstanceType::Part || instance.type == InstanceType::SpawnLocation) {
      DrawPart(instance);
    }
    for (const auto& child : instance.children) draw(*child);
  };
  draw(*model.Workspace());
}

void RendererD3D9::RenderClassicR6(const Vec3& position, float yaw, float animationClock, const std::string& animation) {
  if (!device_ || !sceneOpen_) return;
  const float swing = animation == "walk" ? std::sin(animationClock * 8.0f) : 0.0f;
  const float jumpArm = animation == "jump" ? DegToRad(-115.0f) : 0.0f;
  const Color yellow = ColorFromHex("#F5CD30");
  const Color torso = ColorFromHex("#C4281C");
  const Color legs = ColorFromHex("#1B2A35");
  const Color arms = ColorFromHex("#1B2A35");

  DrawBox({ position.x, position.y + 3.5f, position.z }, { 0, yaw, 0 }, { 2.0f, 1.2f, 2.0f }, yellow);
  DrawBox({ position.x, position.y + 2.25f, position.z }, { 0, yaw, 0 }, { 2.4f, 2.2f, 1.2f }, torso);
  DrawBox({ position.x - 1.8f, position.y + 2.15f, position.z }, { jumpArm + swing * 0.5f, yaw, 0 }, { 0.8f, 2.2f, 0.8f }, arms);
  DrawBox({ position.x + 1.8f, position.y + 2.15f, position.z }, { jumpArm - swing * 0.5f, yaw, 0 }, { 0.8f, 2.2f, 0.8f }, arms);
  DrawBox({ position.x - 0.55f, position.y + 0.7f, position.z }, { -swing * 0.35f, yaw, 0 }, { 0.95f, 1.5f, 0.9f }, legs);
  DrawBox({ position.x + 0.55f, position.y + 0.7f, position.z }, { swing * 0.35f, yaw, 0 }, { 0.95f, 1.5f, 0.9f }, legs);
}

void RendererD3D9::DrawPart(const Instance& part) {
  DrawBox(part.transform.position, {
    DegToRad(part.transform.rotation.x),
    DegToRad(part.transform.rotation.y),
    DegToRad(part.transform.rotation.z)
  }, part.transform.size, part.color);
  DrawStuds(part);
}

void RendererD3D9::DrawBox(const Vec3& position, const Vec3& rotation, const Vec3& size, const Color& color) {
  const D3DMATRIX world = Multiply(
    Multiply(
      Multiply(Scale(size.x, size.y, size.z), RotationX(rotation.x)),
      Multiply(RotationY(rotation.y), RotationZ(rotation.z))),
    Translation(position.x, position.y, position.z));
  device_->SetTransform(D3DTS_WORLD, &world);
  const auto vertices = CubeVertices(PackColor(color));
  device_->SetFVF(Vertex::FVF);
  device_->DrawPrimitiveUP(D3DPT_TRIANGLELIST, 12, vertices.data(), sizeof(Vertex));
}

void RendererD3D9::DrawStuds(const Instance& part) {
  if (part.transform.size.x < 2.0f || part.transform.size.z < 2.0f || part.transform.size.y < 0.1f) return;
  const int studsX = std::min(24, std::max(0, static_cast<int>(part.transform.size.x)));
  const int studsZ = std::min(24, std::max(0, static_cast<int>(part.transform.size.z)));
  const float startX = -studsX * 0.5f + 0.5f;
  const float startZ = -studsZ * 0.5f + 0.5f;
  Color stud = part.color;
  stud.r = std::min(1.0f, stud.r + 0.12f);
  stud.g = std::min(1.0f, stud.g + 0.12f);
  stud.b = std::min(1.0f, stud.b + 0.12f);
  for (int x = 0; x < studsX; x += 2) {
    for (int z = 0; z < studsZ; z += 2) {
      DrawBox({
        part.transform.position.x + startX + x,
        part.transform.position.y + part.transform.size.y * 0.5f + 0.04f,
        part.transform.position.z + startZ + z
      }, { 0, 0, 0 }, { 0.55f, 0.08f, 0.55f }, stud);
    }
  }
}

void RendererD3D9::EndFrame() {
  if (!device_ || !sceneOpen_) return;
  device_->EndScene();
  device_->Present(nullptr, nullptr, nullptr, nullptr);
  sceneOpen_ = false;
}

}
