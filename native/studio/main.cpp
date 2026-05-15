#include "Novus/Application.h"
#include "Novus/HttpClient.h"

#include <algorithm>
#include <cmath>
#include <shellapi.h>
#include <sstream>
#include <windowsx.h>

using namespace Novus;

namespace {

enum CommandId {
  CmdNew = 100,
  CmdOpenDashboard,
  CmdSave,
  CmdPublish,
  CmdRun,
  CmdStop,
  CmdSelect,
  CmdMove,
  CmdRotate,
  CmdScale,
  CmdPart,
  CmdSpawn,
  CmdScript,
  CmdAnchor,
  CmdCollide
};

struct Button {
  RECT rect{};
  int id = 0;
  std::wstring text;
};

class StudioApp final : public ClassicApp {
public:
  StudioApp() : ClassicApp(L"Novus Worlds Studio") {}

protected:
  bool OnCreate() override {
    options_ = ParseLaunchOptions();
    LoadProject();
    BuildButtons();
    return true;
  }

  void OnResize(int width, int height) override {
    ClassicApp::OnResize(width, height);
    BuildButtons();
  }

  void OnTick(float dt) override {
    animationClock_ += dt;
    if (runMode_) {
      testPlayer_.position.x += std::sin(animationClock_ * 0.7f) * dt * 1.5f;
      testPlayer_.animation = "walk";
    }
  }

  void OnRender() override {
    Renderer().BeginFrame(ColorFromHex("#A8C9ED"));
    Renderer().RenderDataModel(model_, camera_);
    if (runMode_) Renderer().RenderClassicR6(testPlayer_.position, 0.0f, animationClock_, testPlayer_.animation);
    Renderer().EndFrame();
    DrawStudioChrome();
  }

  void OnKey(UINT key, bool down) override {
    if (!down) return;
    if (key == VK_DELETE) DeleteSelected();
    if (key == 'F') FocusSelection();
    if (key == 'S' && (GetKeyState(VK_CONTROL) & 0x8000)) SaveProject(false);
    if (key == 'D' && (GetKeyState(VK_CONTROL) & 0x8000)) DuplicateSelected();
    if (selected_) {
      const float step = 1.0f;
      if (tool_ == "Move") {
        if (key == VK_LEFT) selected_->transform.position.x -= step;
        if (key == VK_RIGHT) selected_->transform.position.x += step;
        if (key == VK_UP) selected_->transform.position.z += step;
        if (key == VK_DOWN) selected_->transform.position.z -= step;
        if (key == VK_PRIOR) selected_->transform.position.y += step;
        if (key == VK_NEXT) selected_->transform.position.y -= step;
      }
      if (tool_ == "Scale") {
        if (key == VK_LEFT) selected_->transform.size.x = std::max(0.25f, selected_->transform.size.x - step);
        if (key == VK_RIGHT) selected_->transform.size.x += step;
        if (key == VK_UP) selected_->transform.size.z += step;
        if (key == VK_DOWN) selected_->transform.size.z = std::max(0.25f, selected_->transform.size.z - step);
      }
      if (tool_ == "Rotate") {
        if (key == VK_LEFT) selected_->transform.rotation.y -= 5.0f;
        if (key == VK_RIGHT) selected_->transform.rotation.y += 5.0f;
      }
    }
  }

  void OnMouseMove(int x, int y, WPARAM buttons) override {
    if (buttons & MK_RBUTTON) {
      camera_.yaw += (x - lastMouseX_) * 0.008f;
      camera_.pitch = std::clamp(camera_.pitch + (y - lastMouseY_) * 0.006f, -0.25f, 1.4f);
    }
    lastMouseX_ = x;
    lastMouseY_ = y;
  }

  void OnLButtonDown(int x, int y) override {
    for (const auto& button : buttons_) {
      if (PtInRect(&button.rect, POINT{ x, y })) {
        Execute(button.id);
        return;
      }
    }
    if (x > 220 && x < Renderer().Width() - 280 && y > 112 && y < Renderer().Height() - 28) {
      SelectNextPart();
    }
  }

private:
  struct TestPlayer {
    Vec3 position = { 0.0f, 1.0f, 0.0f };
    std::string animation = "idle";
  };

  void LoadProject() {
    model_ = DataModel::CreateEmptyPlace("Novo Mundo");
    if (options_.projectJsonPath.empty()) return;
    try {
      const Json project = Json::Parse(ReadTextFile(options_.projectJsonPath));
      title_ = project.Get("title").AsString("Novo Mundo");
      model_ = DataModel::FromLegacyPlaceJson(project.Get("map"));
      status_ = "Projeto carregado.";
    } catch (...) {
      status_ = "Erro ao abrir projeto; usando baseplate.";
    }
  }

  void BuildButtons() {
    buttons_.clear();
    int x = 6;
    auto add = [&](int id, const wchar_t* text, int width) {
      RECT r{ x, 30, x + width, 56 };
      buttons_.push_back({ r, id, text });
      x += width + 4;
    };
    add(CmdNew, L"New", 48);
    add(CmdOpenDashboard, L"Dashboard", 92);
    add(CmdSave, L"Save", 54);
    add(CmdPublish, L"Publish", 72);
    add(CmdRun, L"Run", 52);
    add(CmdStop, L"Stop", 52);
    add(CmdSelect, L"Select", 66);
    add(CmdMove, L"Move", 58);
    add(CmdRotate, L"Rotate", 70);
    add(CmdScale, L"Scale", 62);
    add(CmdPart, L"Part", 54);
    add(CmdSpawn, L"Spawn", 66);
    add(CmdScript, L"Script", 66);
    add(CmdAnchor, L"Anchor", 72);
    add(CmdCollide, L"Collide", 74);
  }

  void Execute(int id) {
    switch (id) {
    case CmdNew:
      model_ = DataModel::CreateEmptyPlace("Novo Mundo");
      selected_ = nullptr;
      status_ = "Novo projeto criado.";
      break;
    case CmdOpenDashboard:
      ShellExecuteW(nullptr, L"open", ToWide(options_.baseUrl + "/studio-dashboard.html").c_str(), nullptr, nullptr, SW_SHOWNORMAL);
      break;
    case CmdSave:
      SaveProject(false);
      break;
    case CmdPublish:
      SaveProject(true);
      break;
    case CmdRun:
      runMode_ = true;
      SpawnTestPlayer();
      status_ = "Play solo iniciado no Studio.";
      break;
    case CmdStop:
      runMode_ = false;
      status_ = "Play solo parado.";
      break;
    case CmdSelect: tool_ = "Select"; break;
    case CmdMove: tool_ = "Move"; break;
    case CmdRotate: tool_ = "Rotate"; break;
    case CmdScale: tool_ = "Scale"; break;
    case CmdPart: InsertPart(); break;
    case CmdSpawn: InsertSpawn(); break;
    case CmdScript: InsertScript(); break;
    case CmdAnchor:
      if (selected_) selected_->anchored = !selected_->anchored;
      break;
    case CmdCollide:
      if (selected_) selected_->canCollide = !selected_->canCollide;
      break;
    }
  }

  void InsertPart() {
    auto part = std::make_shared<Instance>(InstanceType::Part, "Part");
    part->transform.position = { 0.0f, 2.0f, 0.0f };
    part->transform.size = { 4.0f, 1.2f, 4.0f };
    part->color = ColorFromHex("#C4281C");
    model_.Workspace()->AddChild(part);
    selected_ = part.get();
    status_ = "Part inserida.";
  }

  void InsertSpawn() {
    auto spawn = std::make_shared<Instance>(InstanceType::SpawnLocation, "SpawnLocation");
    spawn->transform.position = { 0.0f, 0.4f, 0.0f };
    spawn->transform.size = { 6.0f, 0.4f, 6.0f };
    spawn->color = ColorFromHex("#C83232");
    model_.Workspace()->AddChild(spawn);
    selected_ = spawn.get();
    status_ = "SpawnLocation inserido.";
  }

  void InsertScript() {
    auto script = std::make_shared<Instance>(InstanceType::Script, "Script");
    script->source = "game.on('playerJoin', function(player)\n  player:setHealth(100)\nend)";
    model_.Service(InstanceType::ServerScriptService)->AddChild(script);
    selected_ = script.get();
    status_ = "Script inserido em ServerScriptService.";
  }

  void SelectNextPart() {
    auto workspace = model_.Workspace();
    if (!workspace || workspace->children.empty()) return;
    size_t index = 0;
    for (size_t i = 0; i < workspace->children.size(); ++i) {
      if (workspace->children[i].get() == selected_) {
        index = (i + 1) % workspace->children.size();
        break;
      }
    }
    selected_ = workspace->children[index].get();
    status_ = "Selecionado: " + selected_->name;
  }

  void DeleteSelected() {
    if (!selected_) return;
    auto workspace = model_.Workspace();
    auto& children = workspace->children;
    children.erase(std::remove_if(children.begin(), children.end(), [&](const auto& child) { return child.get() == selected_; }), children.end());
    selected_ = nullptr;
    status_ = "Objeto removido.";
  }

  void DuplicateSelected() {
    if (!selected_) return;
    Json copyJson = InstanceToJson(*selected_);
    auto copy = InstanceFromJson(copyJson);
    copy->id = selected_->id + "_copy";
    copy->name += " Copy";
    copy->transform.position.x += 2.0f;
    copy->transform.position.z += 2.0f;
    model_.Workspace()->AddChild(copy);
    selected_ = copy.get();
    status_ = "Objeto duplicado.";
  }

  void FocusSelection() {
    if (!selected_) return;
    camera_.target = selected_->transform.position;
    camera_.distance = std::max(12.0f, selected_->transform.size.x + selected_->transform.size.z + 8.0f);
  }

  void SpawnTestPlayer() {
    testPlayer_.position = { 0.0f, 1.0f, 0.0f };
    if (auto workspace = model_.Workspace()) {
      for (const auto& child : workspace->children) {
        if (child->type == InstanceType::SpawnLocation) {
          testPlayer_.position = { child->transform.position.x, child->transform.position.y + 1.0f, child->transform.position.z };
          return;
        }
      }
    }
  }

  void SaveProject(bool publish) {
    const Json dataModel = model_.ToJson();
    if (options_.ticket.empty()) {
      WriteTextFile("NovusWorldsLocalProject.nwm", dataModel.Dump(2));
      status_ = "Sem ticket; salvo localmente em NovusWorldsLocalProject.nwm.";
      return;
    }
    Json::Object body{
      { "ticket", options_.ticket },
      { "title", title_ },
      { "description", "Projeto criado no Novus Worlds Native Studio" },
      { "publish", publish },
      { "maxPlayers", 20 },
      { "map_data", dataModel }
    };
    const HttpResponse res = HttpPostJson(options_.baseUrl + "/api/legacy/studio-project/save", Json(body).Dump());
    if (res.status >= 200 && res.status < 300) {
      status_ = publish ? "Publicado no site." : "Salvo no site.";
    } else {
      std::ostringstream ss;
      ss << "Erro ao salvar: HTTP " << res.status << " " << res.error;
      status_ = ss.str();
    }
  }

  void DrawStudioChrome() {
    HDC dc = GetDC(Window());
    RECT rect{};
    GetClientRect(Window(), &rect);
    SetBkMode(dc, TRANSPARENT);
    HFONT font = CreateFontW(16, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, NONANTIALIASED_QUALITY, DEFAULT_PITCH, L"Arial");
    HFONT old = static_cast<HFONT>(SelectObject(dc, font));

    HBRUSH top = CreateSolidBrush(RGB(236, 236, 236));
    HBRUSH toolbar = CreateSolidBrush(RGB(204, 211, 216));
    HBRUSH panel = CreateSolidBrush(RGB(225, 233, 238));
    HBRUSH dark = CreateSolidBrush(RGB(42, 51, 60));
    RECT menuRect{ 0, 0, rect.right, 28 };
    RECT toolbarRect{ 0, 28, rect.right, 64 };
    FillRect(dc, &menuRect, top);
    FillRect(dc, &toolbarRect, toolbar);

    SetTextColor(dc, RGB(20, 20, 20));
    TextOutW(dc, 8, 6, L"File    Edit    View    Insert    Format    Tools    Window    Help", 66);
    for (const auto& button : buttons_) {
      FillRect(dc, &button.rect, dark);
      SetTextColor(dc, RGB(255, 255, 255));
      TextOutW(dc, button.rect.left + 7, button.rect.top + 5, button.text.c_str(), static_cast<int>(button.text.size()));
    }

    RECT toolbox{ 8, 72, 210, rect.bottom - 34 };
    RECT explorer{ rect.right - 270, 72, rect.right - 8, rect.bottom / 2 };
    RECT props{ rect.right - 270, rect.bottom / 2 + 8, rect.right - 8, rect.bottom - 34 };
    FillRect(dc, &toolbox, panel);
    FillRect(dc, &explorer, panel);
    FillRect(dc, &props, panel);
    FrameRect(dc, &toolbox, dark);
    FrameRect(dc, &explorer, dark);
    FrameRect(dc, &props, dark);

    SetTextColor(dc, RGB(10, 25, 40));
    TextOutW(dc, toolbox.left + 8, toolbox.top + 8, L"Toolbox", 7);
    TextOutW(dc, toolbox.left + 8, toolbox.top + 34, L"Use toolbar: Part, Spawn, Script", 32);
    TextOutW(dc, toolbox.left + 8, toolbox.top + 58, L"Move: arrows/PageUp/PageDown", 27);
    TextOutW(dc, toolbox.left + 8, toolbox.top + 82, L"F focus, Del delete, Ctrl+S save", 32);

    TextOutW(dc, explorer.left + 8, explorer.top + 8, L"Explorer", 8);
    DrawExplorer(dc, explorer.left + 8, explorer.top + 34);
    TextOutW(dc, props.left + 8, props.top + 8, L"Properties", 10);
    DrawProperties(dc, props.left + 8, props.top + 34);

    RECT bottom{ 0, rect.bottom - 26, rect.right, rect.bottom };
    FillRect(dc, &bottom, top);
    std::ostringstream ss;
    ss << model_.CountInstances() << " instances | tool " << tool_ << " | " << status_;
    const std::wstring status = ToWide(ss.str());
    SetTextColor(dc, RGB(20, 20, 20));
    TextOutW(dc, 8, rect.bottom - 20, status.c_str(), static_cast<int>(status.size()));

    DeleteObject(dark);
    DeleteObject(panel);
    DeleteObject(toolbar);
    DeleteObject(top);
    SelectObject(dc, old);
    DeleteObject(font);
    ReleaseDC(Window(), dc);
  }

  void DrawExplorer(HDC dc, int x, int y) {
    const auto drawNode = [&](const Instance& node, int depth, int& yy, const auto& self) -> void {
      if (yy > Renderer().Height() - 60) return;
      const std::wstring line = ToWide(std::string(depth * 2, ' ') + ToString(node.type) + " " + node.name);
      SetTextColor(dc, selected_ == &node ? RGB(185, 0, 0) : RGB(20, 20, 20));
      TextOutW(dc, x, yy, line.c_str(), static_cast<int>(line.size()));
      yy += 20;
      for (const auto& child : node.children) self(*child, depth + 1, yy, self);
    };
    int yy = y;
    drawNode(*model_.Root(), 0, yy, drawNode);
  }

  void DrawProperties(HDC dc, int x, int y) {
    if (!selected_) {
      TextOutW(dc, x, y, L"Nenhum objeto selecionado", 24);
      return;
    }
    std::ostringstream ss;
    ss << selected_->name << "\n"
       << "ClassName: " << ToString(selected_->type) << "\n"
       << "Position: " << selected_->transform.position.x << ", " << selected_->transform.position.y << ", " << selected_->transform.position.z << "\n"
       << "Size: " << selected_->transform.size.x << ", " << selected_->transform.size.y << ", " << selected_->transform.size.z << "\n"
       << "Anchored: " << (selected_->anchored ? "true" : "false") << "\n"
       << "CanCollide: " << (selected_->canCollide ? "true" : "false");
    std::istringstream lines(ss.str());
    std::string line;
    int yy = y;
    while (std::getline(lines, line)) {
      const std::wstring wide = ToWide(line);
      TextOutW(dc, x, yy, wide.c_str(), static_cast<int>(wide.size()));
      yy += 22;
    }
  }

  LaunchOptions options_;
  DataModel model_;
  Camera camera_;
  TestPlayer testPlayer_;
  std::vector<Button> buttons_;
  Instance* selected_ = nullptr;
  std::string title_ = "Novo Mundo";
  std::string tool_ = "Select";
  std::string status_ = "Ready";
  bool runMode_ = false;
  int lastMouseX_ = 0;
  int lastMouseY_ = 0;
  float animationClock_ = 0.0f;
};

}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int showCommand) {
  StudioApp app;
  return app.Run(instance, showCommand);
}
