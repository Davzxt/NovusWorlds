#include "Novus/Application.h"

#include <chrono>
#include <exception>
#include <fstream>
#include <shellapi.h>
#include <sstream>
#include <windowsx.h>

namespace Novus {
namespace {

void NativeLog(const std::string& message) {
  char localAppData[MAX_PATH]{};
  DWORD length = GetEnvironmentVariableA("LOCALAPPDATA", localAppData, MAX_PATH);
  if (length == 0 || length >= MAX_PATH) return;
  const std::string root = std::string(localAppData) + "\\NovusWorlds";
  const std::string dir = root + "\\Cache";
  CreateDirectoryA(root.c_str(), nullptr);
  CreateDirectoryA(dir.c_str(), nullptr);
  std::ofstream out(dir + "\\native-app.log", std::ios::app);
  if (out) out << message << "\n";
}

}

std::wstring ToWide(const std::string& value) {
  if (value.empty()) return {};
  const int length = MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, nullptr, 0);
  std::wstring out(static_cast<size_t>(length), L'\0');
  MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, out.data(), length);
  out.pop_back();
  return out;
}

std::string ToUtf8(const std::wstring& value) {
  if (value.empty()) return {};
  const int length = WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, nullptr, 0, nullptr, nullptr);
  std::string out(static_cast<size_t>(length), '\0');
  WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, out.data(), length, nullptr, nullptr);
  out.pop_back();
  return out;
}

std::string ReadTextFile(const std::string& path) {
  std::ifstream in(path, std::ios::binary);
  if (!in) return {};
  std::ostringstream ss;
  ss << in.rdbuf();
  return ss.str();
}

bool WriteTextFile(const std::string& path, const std::string& text) {
  std::ofstream out(path, std::ios::binary);
  if (!out) return false;
  out << text;
  return true;
}

LaunchOptions ParseLaunchOptions() {
  LaunchOptions options;
  int argc = 0;
  LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
  for (int i = 1; i < argc; ++i) {
    const std::string key = ToUtf8(argv[i]);
    auto next = [&]() -> std::string {
      if (i + 1 >= argc) return {};
      return ToUtf8(argv[++i]);
    };
    if (key == "--base-url") options.baseUrl = next();
    else if (key == "--ticket") options.ticket = next();
    else if (key == "--game") options.gameId = next();
    else if (key == "--join-json") options.joinJsonPath = next();
    else if (key == "--project-json") options.projectJsonPath = next();
  }
  if (argv) LocalFree(argv);
  return options;
}

ClassicApp::ClassicApp(std::wstring title) : title_(std::move(title)) {}

ClassicApp::~ClassicApp() {
  renderer_.Shutdown();
}

int ClassicApp::Run(HINSTANCE instance, int showCommand) {
  NativeLog("App run start");
  if (!instance) instance = GetModuleHandleW(nullptr);
  WNDCLASSW wc{};
  wc.lpfnWndProc = StaticWndProc;
  wc.hInstance = instance;
  wc.lpszClassName = L"NovusWorldsNativeWindow";
  wc.hCursor = LoadCursor(nullptr, IDC_ARROW);
  wc.hIcon = LoadIcon(nullptr, IDI_APPLICATION);
  wc.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_WINDOW + 1);
  ATOM klass = RegisterClassW(&wc);
  if (!klass) {
    const DWORD err = GetLastError();
    if (err != ERROR_CLASS_ALREADY_EXISTS) {
      NativeLog("RegisterClass failed: " + std::to_string(err));
      MessageBoxW(nullptr, L"Nao foi possivel registrar a janela do Novus Worlds.", title_.c_str(), MB_OK | MB_ICONERROR);
      return 1;
    }
    NativeLog("RegisterClass already exists");
  }

  hwnd_ = CreateWindowExW(
    0,
    wc.lpszClassName,
    title_.c_str(),
    WS_OVERLAPPEDWINDOW,
    CW_USEDEFAULT,
    CW_USEDEFAULT,
    1280,
    820,
    nullptr,
    nullptr,
    instance,
    this);

  if (!hwnd_) {
    NativeLog("CreateWindowEx failed: " + std::to_string(GetLastError()));
    MessageBoxW(nullptr, L"Nao foi possivel abrir a janela do Novus Worlds.", title_.c_str(), MB_OK | MB_ICONERROR);
    return 1;
  }
  ShowWindow(hwnd_, showCommand);
  UpdateWindow(hwnd_);

  if (!renderer_.Initialize(hwnd_)) {
    NativeLog("Renderer initialize failed");
    MessageBoxW(hwnd_, L"Nao foi possivel iniciar o Direct3D9. Atualize o Windows/driver de video e tente novamente.", title_.c_str(), MB_OK | MB_ICONERROR);
    return 1;
  }
  try {
    if (!OnCreate()) {
      NativeLog("OnCreate returned false");
      MessageBoxW(hwnd_, L"O aplicativo nao conseguiu iniciar.", title_.c_str(), MB_OK | MB_ICONERROR);
      return 1;
    }
  } catch (const std::exception& err) {
    NativeLog(std::string("OnCreate exception: ") + err.what());
    MessageBoxW(hwnd_, ToWide(err.what()).c_str(), title_.c_str(), MB_OK | MB_ICONERROR);
    return 1;
  } catch (...) {
    NativeLog("OnCreate unknown exception");
    MessageBoxW(hwnd_, L"Erro desconhecido ao iniciar o aplicativo.", title_.c_str(), MB_OK | MB_ICONERROR);
    return 1;
  }

  NativeLog("App main loop");
  MSG msg{};
  auto previous = std::chrono::steady_clock::now();
  while (msg.message != WM_QUIT) {
    while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) {
      TranslateMessage(&msg);
      DispatchMessageW(&msg);
    }
    const auto now = std::chrono::steady_clock::now();
    const float dt = std::chrono::duration<float>(now - previous).count();
    previous = now;
    OnTick(dt);
    OnRender();
    Sleep(1);
  }
  return static_cast<int>(msg.wParam);
}

bool ClassicApp::OnCreate() { return true; }
void ClassicApp::OnTick(float) {}
void ClassicApp::OnRender() {}
void ClassicApp::OnResize(int width, int height) { renderer_.Resize(width, height); }
void ClassicApp::OnKey(UINT, bool) {}
void ClassicApp::OnMouseMove(int, int, WPARAM) {}
void ClassicApp::OnLButtonDown(int, int) {}
void ClassicApp::OnLButtonUp(int, int) {}
void ClassicApp::OnCommand(int) {}

LRESULT CALLBACK ClassicApp::StaticWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {
  auto* app = reinterpret_cast<ClassicApp*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
  if (msg == WM_NCCREATE) {
    auto* create = reinterpret_cast<CREATESTRUCTW*>(lParam);
    app = reinterpret_cast<ClassicApp*>(create->lpCreateParams);
    if (app) app->hwnd_ = hwnd;
    SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(app));
  }
  return app ? app->WndProc(msg, wParam, lParam) : DefWindowProcW(hwnd, msg, wParam, lParam);
}

LRESULT ClassicApp::WndProc(UINT msg, WPARAM wParam, LPARAM lParam) {
  switch (msg) {
  case WM_SIZE:
    OnResize(LOWORD(lParam), HIWORD(lParam));
    return 0;
  case WM_KEYDOWN:
    OnKey(static_cast<UINT>(wParam), true);
    return 0;
  case WM_KEYUP:
    OnKey(static_cast<UINT>(wParam), false);
    return 0;
  case WM_MOUSEMOVE:
    OnMouseMove(GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam), wParam);
    return 0;
  case WM_LBUTTONDOWN:
    SetCapture(hwnd_);
    OnLButtonDown(GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam));
    return 0;
  case WM_LBUTTONUP:
    ReleaseCapture();
    OnLButtonUp(GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam));
    return 0;
  case WM_COMMAND:
    OnCommand(LOWORD(wParam));
    return 0;
  case WM_DESTROY:
    PostQuitMessage(0);
    return 0;
  default:
    return DefWindowProcW(hwnd_, msg, wParam, lParam);
  }
}

}
