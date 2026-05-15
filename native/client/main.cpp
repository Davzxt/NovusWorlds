#include "Novus/Application.h"
#include "Novus/HttpClient.h"

#include <algorithm>
#include <cmath>
#include <sstream>
#include <windowsx.h>

using namespace Novus;

namespace {

struct PlayerState {
  Vec3 position = { 0.0f, 0.0f, 0.0f };
  Vec3 velocity = {};
  float yaw = 0.0f;
  bool grounded = true;
  std::string animation = "idle";
};

class ClientApp final : public ClassicApp {
public:
  ClientApp() : ClassicApp(L"Novus Worlds Client") {}

protected:
  bool OnCreate() override {
    options_ = ParseLaunchOptions();
    LoadPlace();
    status_ = "Bem-vindo ao Novus Worlds.";
    return true;
  }

  void OnTick(float dt) override {
    animationClock_ += dt;
    UpdateMovement(dt);
    camera_.target = { player_.position.x, player_.position.y + 2.3f, player_.position.z };
  }

  void OnRender() override {
    Renderer().BeginFrame(ColorFromHex("#9FC0EA"));
    Renderer().RenderDataModel(model_, camera_);
    Renderer().RenderClassicR6(player_.position, player_.yaw, animationClock_, player_.animation);
    Renderer().EndFrame();
    DrawHud();
  }

  void OnKey(UINT key, bool down) override {
    if (key < 256) keys_[key] = down;
    if (down && key == VK_ESCAPE) PostMessageW(Window(), WM_CLOSE, 0, 0);
    if (down && key == 'R') ResetCharacter();
    if (down && key == VK_SPACE && player_.grounded) {
      player_.velocity.y = 11.2f;
      player_.grounded = false;
      player_.animation = "jump";
    }
  }

  void OnMouseMove(int x, int y, WPARAM buttons) override {
    if (buttons & MK_RBUTTON) {
      camera_.yaw += (x - lastMouseX_) * 0.008f;
      camera_.pitch = std::clamp(camera_.pitch + (y - lastMouseY_) * 0.006f, -0.15f, 1.25f);
    }
    lastMouseX_ = x;
    lastMouseY_ = y;
  }

private:
  void LoadPlace() {
    model_ = DataModel::CreateEmptyPlace("Classic Baseplate");
    username_ = "NovusPlayer";
    try {
      if (!options_.joinJsonPath.empty()) {
        const Json join = Json::Parse(ReadTextFile(options_.joinJsonPath));
        username_ = join.Get("username").AsString(username_);
        const std::string placeUrl = join.Get("placeUrl").AsString("");
        if (!placeUrl.empty()) {
          const HttpResponse place = HttpGet(placeUrl);
          if (place.status >= 200 && place.status < 300 && !place.body.empty()) {
            model_ = DataModel::FromLegacyPlaceJson(Json::Parse(place.body));
            status_ = "Mapa carregado do site.";
          } else {
            status_ = "Falha ao baixar mapa; usando baseplate.";
          }
        }
      } else {
        const std::string url = options_.baseUrl + "/api/legacy/place/" + options_.gameId;
        const HttpResponse place = HttpGet(url);
        if (place.status >= 200 && place.status < 300 && !place.body.empty()) {
          model_ = DataModel::FromLegacyPlaceJson(Json::Parse(place.body));
        }
      }
    } catch (...) {
      status_ = "Erro ao ler ticket; usando baseplate.";
    }
    SpawnAtFirstSpawn();
  }

  void SpawnAtFirstSpawn() {
    if (auto workspace = model_.Workspace()) {
      for (const auto& child : workspace->children) {
        if (child->type == InstanceType::SpawnLocation) {
          player_.position = { child->transform.position.x, child->transform.position.y + 1.0f, child->transform.position.z };
          return;
        }
      }
    }
    player_.position = { 0.0f, 1.0f, 0.0f };
  }

  void ResetCharacter() {
    player_.velocity = {};
    player_.grounded = true;
    player_.animation = "idle";
    SpawnAtFirstSpawn();
  }

  void UpdateMovement(float dt) {
    Vec3 input{};
    if (keys_['W']) input.z += 1.0f;
    if (keys_['S']) input.z -= 1.0f;
    if (keys_['A']) input.x -= 1.0f;
    if (keys_['D']) input.x += 1.0f;
    if (keys_[VK_LEFT]) camera_.yaw -= dt * 1.7f;
    if (keys_[VK_RIGHT]) camera_.yaw += dt * 1.7f;
    if (keys_[VK_UP]) camera_.pitch = std::clamp(camera_.pitch - dt * 1.2f, -0.15f, 1.25f);
    if (keys_[VK_DOWN]) camera_.pitch = std::clamp(camera_.pitch + dt * 1.2f, -0.15f, 1.25f);
    if (keys_['Z']) camera_.distance = std::clamp(camera_.distance - dt * 20.0f, 8.0f, 80.0f);
    if (keys_['X']) camera_.distance = std::clamp(camera_.distance + dt * 20.0f, 8.0f, 80.0f);

    const float length = std::sqrt(input.x * input.x + input.z * input.z);
    if (length > 0.01f) {
      input.x /= length;
      input.z /= length;
      const float sinYaw = std::sin(camera_.yaw);
      const float cosYaw = std::cos(camera_.yaw);
      const Vec3 forward = { sinYaw, 0.0f, cosYaw };
      const Vec3 right = { cosYaw, 0.0f, -sinYaw };
      const float speed = 12.0f;
      player_.position.x += (forward.x * input.z + right.x * input.x) * speed * dt;
      player_.position.z += (forward.z * input.z + right.z * input.x) * speed * dt;
      player_.yaw = std::atan2(forward.x * input.z + right.x * input.x, forward.z * input.z + right.z * input.x);
      if (player_.grounded) player_.animation = "walk";
    } else if (player_.grounded) {
      player_.animation = "idle";
    }

    player_.velocity.y -= 31.0f * dt;
    player_.position.y += player_.velocity.y * dt;
    if (player_.position.y <= 1.0f) {
      player_.position.y = 1.0f;
      player_.velocity.y = 0.0f;
      player_.grounded = true;
    }
    if (!player_.grounded && player_.velocity.y < -2.0f) player_.animation = "fall";
    if (player_.position.y < -80.0f) ResetCharacter();
  }

  void DrawHud() {
    HDC dc = GetDC(Window());
    RECT rect{};
    GetClientRect(Window(), &rect);
    SetBkMode(dc, TRANSPARENT);
    HFONT font = CreateFontW(16, 0, 0, 0, FW_BOLD, FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, NONANTIALIASED_QUALITY, DEFAULT_PITCH, L"Arial");
    HFONT old = static_cast<HFONT>(SelectObject(dc, font));

    HBRUSH gray = CreateSolidBrush(RGB(185, 192, 198));
    RECT top{ 0, 0, rect.right, 24 };
    FillRect(dc, &top, gray);
    SetTextColor(dc, RGB(255, 255, 255));
    TextOutW(dc, 8, 3, L"Novus Worlds", 12);
    SetTextColor(dc, RGB(40, 40, 40));
    TextOutW(dc, 250, 3, L"Reset", 5);
    TextOutW(dc, 348, 3, L"Help", 4);
    TextOutW(dc, 432, 3, L"Exit", 4);

    SetTextColor(dc, RGB(255, 255, 255));
    std::wstring status = ToWide(status_);
    TextOutW(dc, 8, 34, status.c_str(), static_cast<int>(status.size()));

    RECT list{ rect.right - 220, 40, rect.right - 16, 128 };
    HBRUSH panel = CreateSolidBrush(RGB(92, 104, 119));
    FillRect(dc, &list, panel);
    SetTextColor(dc, RGB(255, 255, 255));
    TextOutW(dc, list.left + 8, list.top + 8, L"Players", 7);
    SetTextColor(dc, RGB(255, 210, 50));
    std::wstring username = ToWide(username_);
    TextOutW(dc, list.left + 8, list.top + 34, username.c_str(), static_cast<int>(username.size()));

    RECT hotbar{ rect.right / 2 - 150, rect.bottom - 78, rect.right / 2 + 150, rect.bottom - 24 };
    HBRUSH dark = CreateSolidBrush(RGB(20, 20, 20));
    FillRect(dc, &hotbar, dark);
    for (int i = 0; i < 5; ++i) {
      RECT slot{ hotbar.left + 8 + i * 56, hotbar.top + 8, hotbar.left + 52 + i * 56, hotbar.bottom - 8 };
      FrameRect(dc, &slot, i == 0 ? gray : dark);
      std::wstring label = std::to_wstring(i + 1);
      SetTextColor(dc, RGB(255, 255, 255));
      TextOutW(dc, slot.left + 5, slot.top + 4, label.c_str(), static_cast<int>(label.size()));
    }

    RECT healthOuter{ rect.right / 2 - 80, rect.bottom - 22, rect.right / 2 + 80, rect.bottom - 8 };
    HBRUSH black = CreateSolidBrush(RGB(0, 0, 0));
    HBRUSH green = CreateSolidBrush(RGB(0, 190, 0));
    FillRect(dc, &healthOuter, black);
    RECT healthInner{ healthOuter.left + 2, healthOuter.top + 2, healthOuter.right - 2, healthOuter.bottom - 2 };
    FillRect(dc, &healthInner, green);
    SetTextColor(dc, RGB(255, 255, 255));
    TextOutW(dc, healthOuter.left + 55, healthOuter.top - 1, L"HEALTH", 6);

    RECT chat{ 4, rect.bottom - 24, 520, rect.bottom - 2 };
    FillRect(dc, &chat, black);
    SetTextColor(dc, RGB(220, 220, 220));
    TextOutW(dc, 8, rect.bottom - 22, L"To chat click here or press the '/' key", 39);

    DeleteObject(green);
    DeleteObject(black);
    DeleteObject(dark);
    DeleteObject(panel);
    DeleteObject(gray);
    SelectObject(dc, old);
    DeleteObject(font);
    ReleaseDC(Window(), dc);
  }

  LaunchOptions options_;
  DataModel model_;
  PlayerState player_;
  Camera camera_;
  bool keys_[256]{};
  int lastMouseX_ = 0;
  int lastMouseY_ = 0;
  float animationClock_ = 0.0f;
  std::string username_ = "NovusPlayer";
  std::string status_;
};

}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int showCommand) {
  ClientApp app;
  return app.Run(instance, showCommand);
}
