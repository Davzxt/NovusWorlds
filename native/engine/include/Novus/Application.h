#pragma once

#include "Novus/DataModel.h"
#include "Novus/RendererD3D9.h"

#include <functional>
#include <string>
#include <vector>
#include <windows.h>

namespace Novus {

struct LaunchOptions {
  std::string baseUrl = "http://localhost:3000";
  std::string ticket;
  std::string gameId = "1";
  std::string joinJsonPath;
  std::string projectJsonPath;
};

LaunchOptions ParseLaunchOptions();
std::wstring ToWide(const std::string& value);
std::string ToUtf8(const std::wstring& value);
std::string ReadTextFile(const std::string& path);
bool WriteTextFile(const std::string& path, const std::string& text);

class ClassicApp {
public:
  explicit ClassicApp(std::wstring title);
  virtual ~ClassicApp();

  int Run(HINSTANCE instance, int showCommand);

protected:
  virtual bool OnCreate();
  virtual void OnTick(float dt);
  virtual void OnRender();
  virtual void OnResize(int width, int height);
  virtual void OnKey(UINT key, bool down);
  virtual void OnMouseMove(int x, int y, WPARAM buttons);
  virtual void OnLButtonDown(int x, int y);
  virtual void OnLButtonUp(int x, int y);
  virtual void OnCommand(int id);

  HWND Window() const { return hwnd_; }
  RendererD3D9& Renderer() { return renderer_; }
  const RendererD3D9& Renderer() const { return renderer_; }

private:
  static LRESULT CALLBACK StaticWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam);
  LRESULT WndProc(UINT msg, WPARAM wParam, LPARAM lParam);

  std::wstring title_;
  HWND hwnd_ = nullptr;
  RendererD3D9 renderer_;
};

}
