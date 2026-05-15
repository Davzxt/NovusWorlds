#include "Novus/DataModel.h"

#include <algorithm>
#include <iomanip>
#include <random>
#include <sstream>
#include <utility>

namespace Novus {
namespace {

std::string NewId() {
  static std::mt19937 rng{ std::random_device{}() };
  static std::uniform_int_distribution<int> dist(0, 15);
  std::string id;
  for (int i = 0; i < 32; ++i) id.push_back("0123456789abcdef"[dist(rng)]);
  return id;
}

Vec3 VecFromJson(const Json& json, Vec3 fallback = {}) {
  if (!json.IsObject()) return fallback;
  return {
    static_cast<float>(json.Get("x").AsNumber(fallback.x)),
    static_cast<float>(json.Get("y").AsNumber(fallback.y)),
    static_cast<float>(json.Get("z").AsNumber(fallback.z))
  };
}

Json VecToJson(const Vec3& value) {
  return Json::Object{
    { "x", value.x },
    { "y", value.y },
    { "z", value.z }
  };
}

Json ColorToJson(const Color& value) {
  return HexFromColor(value);
}

int CountChildren(const std::shared_ptr<Instance>& instance) {
  int count = 1;
  for (const auto& child : instance->children) count += CountChildren(child);
  return count;
}

std::shared_ptr<Instance> ServiceNode(InstanceType type, const std::string& name) {
  auto service = std::make_shared<Instance>(type, name);
  service->anchored = true;
  service->canCollide = false;
  return service;
}

std::shared_ptr<Instance> ConvertLegacyObject(const Json& object) {
  auto instance = std::make_shared<Instance>(
    InstanceTypeFromString(object.Get("type").AsString("Part")),
    object.Get("name").AsString("Part"));
  instance->id = object.Get("id").AsString(NewId());
  instance->transform.position = VecFromJson(object.Get("position"));
  instance->transform.rotation = VecFromJson(object.Get("rotation"));
  instance->transform.size = VecFromJson(object.Get("size"), { 4.0f, 1.0f, 2.0f });
  instance->color = ColorFromHex(object.Get("color").AsString("#6B8E23"), instance->color);
  instance->material = object.Get("material").AsString("Plastic");
  instance->anchored = object.Get("anchored").AsBool(true);
  instance->canCollide = object.Get("canCollide").AsBool(true);
  instance->transparency = static_cast<float>(object.Get("transparency").AsNumber(0.0));
  instance->reflectance = static_cast<float>(object.Get("reflectance").AsNumber(0.0));
  for (const auto& child : object.Get("children").AsArray()) instance->AddChild(ConvertLegacyObject(child));
  return instance;
}

}

Color ColorFromHex(const std::string& hex, Color fallback) {
  std::string value = hex;
  if (!value.empty() && value[0] == '#') value.erase(value.begin());
  if (value.size() != 6) return fallback;
  try {
    const int raw = std::stoi(value, nullptr, 16);
    return {
      ((raw >> 16) & 0xff) / 255.0f,
      ((raw >> 8) & 0xff) / 255.0f,
      (raw & 0xff) / 255.0f,
      1.0f
    };
  } catch (...) {
    return fallback;
  }
}

std::string HexFromColor(const Color& color) {
  const auto clamp = [](float v) -> int {
    return std::clamp(static_cast<int>(v * 255.0f), 0, 255);
  };
  std::ostringstream out;
  out << "#" << std::uppercase << std::hex << std::setfill('0')
      << std::setw(2) << clamp(color.r)
      << std::setw(2) << clamp(color.g)
      << std::setw(2) << clamp(color.b);
  return out.str();
}

std::string ToString(InstanceType type) {
  switch (type) {
  case InstanceType::DataModel: return "DataModel";
  case InstanceType::Service: return "Service";
  case InstanceType::Workspace: return "Workspace";
  case InstanceType::Players: return "Players";
  case InstanceType::Lighting: return "Lighting";
  case InstanceType::StarterGui: return "StarterGui";
  case InstanceType::StarterPack: return "StarterPack";
  case InstanceType::ReplicatedStorage: return "ReplicatedStorage";
  case InstanceType::ServerScriptService: return "ServerScriptService";
  case InstanceType::Model: return "Model";
  case InstanceType::Part: return "Part";
  case InstanceType::SpawnLocation: return "SpawnLocation";
  case InstanceType::Script: return "Script";
  case InstanceType::Tool: return "Tool";
  case InstanceType::Decal: return "Decal";
  case InstanceType::PointLight: return "PointLight";
  case InstanceType::SurfaceLight: return "SurfaceLight";
  case InstanceType::Humanoid: return "Humanoid";
  }
  return "Part";
}

InstanceType InstanceTypeFromString(const std::string& value) {
  if (value == "DataModel") return InstanceType::DataModel;
  if (value == "Workspace") return InstanceType::Workspace;
  if (value == "Players") return InstanceType::Players;
  if (value == "Lighting") return InstanceType::Lighting;
  if (value == "StarterGui") return InstanceType::StarterGui;
  if (value == "StarterPack") return InstanceType::StarterPack;
  if (value == "ReplicatedStorage") return InstanceType::ReplicatedStorage;
  if (value == "ServerScriptService") return InstanceType::ServerScriptService;
  if (value == "Model") return InstanceType::Model;
  if (value == "SpawnPoint" || value == "SpawnLocation") return InstanceType::SpawnLocation;
  if (value == "Script") return InstanceType::Script;
  if (value == "Tool") return InstanceType::Tool;
  if (value == "Decal") return InstanceType::Decal;
  if (value == "PointLight") return InstanceType::PointLight;
  if (value == "SurfaceLight") return InstanceType::SurfaceLight;
  if (value == "Humanoid") return InstanceType::Humanoid;
  return InstanceType::Part;
}

Instance::Instance(InstanceType instanceType, std::string instanceName)
  : id(NewId()), name(std::move(instanceName)), type(instanceType) {}

std::shared_ptr<Instance> Instance::AddChild(std::shared_ptr<Instance> child) {
  children.push_back(std::move(child));
  return children.back();
}

Instance* Instance::FindById(const std::string& wantedId) {
  if (id == wantedId) return this;
  for (auto& child : children) {
    if (auto found = child->FindById(wantedId)) return found;
  }
  return nullptr;
}

const Instance* Instance::FindById(const std::string& wantedId) const {
  if (id == wantedId) return this;
  for (const auto& child : children) {
    if (auto found = child->FindById(wantedId)) return found;
  }
  return nullptr;
}

DataModel::DataModel() {
  root_ = std::make_shared<Instance>(InstanceType::DataModel, "Novus Place");
  AddDefaultServices();
}

void DataModel::AddDefaultServices() {
  workspace_ = ServiceNode(InstanceType::Workspace, "Workspace");
  root_->AddChild(workspace_);
  root_->AddChild(ServiceNode(InstanceType::Players, "Players"));
  root_->AddChild(ServiceNode(InstanceType::Lighting, "Lighting"));
  root_->AddChild(ServiceNode(InstanceType::StarterGui, "StarterGui"));
  root_->AddChild(ServiceNode(InstanceType::StarterPack, "StarterPack"));
  root_->AddChild(ServiceNode(InstanceType::ReplicatedStorage, "ReplicatedStorage"));
  root_->AddChild(ServiceNode(InstanceType::ServerScriptService, "ServerScriptService"));
}

std::shared_ptr<Instance> DataModel::Service(InstanceType type) const {
  for (const auto& child : root_->children) {
    if (child->type == type) return child;
  }
  return nullptr;
}

int DataModel::CountInstances() const {
  return CountChildren(root_);
}

Json DataModel::ToJson() const {
  Json::Object out;
  out["format"] = "NovusDataModel";
  out["version"] = 1;
  out["root"] = InstanceToJson(*root_);
  return out;
}

DataModel DataModel::CreateEmptyPlace(const std::string& name) {
  DataModel model;
  model.root_->name = name;
  auto baseplate = std::make_shared<Instance>(InstanceType::Part, "Baseplate");
  baseplate->transform.position = { 0.0f, -0.5f, 0.0f };
  baseplate->transform.size = { 96.0f, 1.0f, 96.0f };
  baseplate->color = ColorFromHex("#2F9E62");
  baseplate->material = "Grass";
  baseplate->anchored = true;
  model.workspace_->AddChild(baseplate);

  auto spawn = std::make_shared<Instance>(InstanceType::SpawnLocation, "SpawnLocation");
  spawn->transform.position = { 0.0f, 0.2f, 0.0f };
  spawn->transform.size = { 6.0f, 0.4f, 6.0f };
  spawn->color = ColorFromHex("#C83232");
  spawn->material = "Plastic";
  spawn->anchored = true;
  model.workspace_->AddChild(spawn);
  return model;
}

DataModel DataModel::FromLegacyPlaceJson(const Json& value) {
  const Json& map = value.Has("map") ? value.Get("map") : value;
  if (map.Get("format").AsString() == "NovusDataModel" || map.Has("root")) return FromDataModelJson(map);

  DataModel model;
  model.root_->name = map.Get("name").AsString(value.Get("title").AsString("Novus Place"));
  model.workspace_->children.clear();
  for (const auto& object : map.Get("objects").AsArray()) {
    model.workspace_->AddChild(ConvertLegacyObject(object));
  }
  for (const auto& spawnJson : map.Get("spawnPoints").AsArray()) {
    auto spawn = std::make_shared<Instance>(InstanceType::SpawnLocation, "SpawnLocation");
    spawn->transform.position = VecFromJson(spawnJson, { 0.0f, 3.0f, 0.0f });
    spawn->transform.size = { 6.0f, 0.4f, 6.0f };
    spawn->color = ColorFromHex("#C83232");
    model.workspace_->AddChild(spawn);
  }
  if (model.workspace_->children.empty()) return CreateEmptyPlace(model.root_->name);
  return model;
}

DataModel DataModel::FromDataModelJson(const Json& value) {
  DataModel model;
  const Json& rootJson = value.Has("root") ? value.Get("root") : value;
  auto root = InstanceFromJson(rootJson);
  if (root && root->type == InstanceType::DataModel) {
    model.root_ = root;
    model.workspace_ = nullptr;
    for (const auto& child : root->children) {
      if (child->type == InstanceType::Workspace) model.workspace_ = child;
    }
    if (!model.workspace_) {
      model.workspace_ = ServiceNode(InstanceType::Workspace, "Workspace");
      model.root_->children.insert(model.root_->children.begin(), model.workspace_);
    }
  }
  return model;
}

Json InstanceToJson(const Instance& instance) {
  Json::Array children;
  for (const auto& child : instance.children) children.push_back(InstanceToJson(*child));
  Json::Object object{
    { "id", instance.id },
    { "name", instance.name },
    { "className", ToString(instance.type) },
    { "position", VecToJson(instance.transform.position) },
    { "rotation", VecToJson(instance.transform.rotation) },
    { "size", VecToJson(instance.transform.size) },
    { "color", ColorToJson(instance.color) },
    { "material", instance.material },
    { "anchored", instance.anchored },
    { "canCollide", instance.canCollide },
    { "visible", instance.visible },
    { "locked", instance.locked },
    { "transparency", instance.transparency },
    { "reflectance", instance.reflectance },
    { "children", children }
  };
  if (!instance.source.empty()) object["source"] = instance.source;
  return object;
}

std::shared_ptr<Instance> InstanceFromJson(const Json& value) {
  if (!value.IsObject()) return nullptr;
  const std::string className = value.Get("className").IsString()
    ? value.Get("className").AsString()
    : value.Get("type").AsString("Part");
  auto instance = std::make_shared<Instance>(
    InstanceTypeFromString(className),
    value.Get("name").AsString("Part"));
  instance->id = value.Get("id").AsString(NewId());
  instance->transform.position = VecFromJson(value.Get("position"));
  instance->transform.rotation = VecFromJson(value.Get("rotation"));
  instance->transform.size = VecFromJson(value.Get("size"), { 1.0f, 1.0f, 1.0f });
  instance->color = ColorFromHex(value.Get("color").AsString("#FFFFFF"), instance->color);
  instance->material = value.Get("material").AsString("Plastic");
  instance->anchored = value.Get("anchored").AsBool(true);
  instance->canCollide = value.Get("canCollide").AsBool(true);
  instance->visible = value.Get("visible").AsBool(true);
  instance->locked = value.Get("locked").AsBool(false);
  instance->transparency = static_cast<float>(value.Get("transparency").AsNumber(0.0));
  instance->reflectance = static_cast<float>(value.Get("reflectance").AsNumber(0.0));
  instance->source = value.Get("source").AsString("");
  for (const auto& child : value.Get("children").AsArray()) {
    if (auto converted = InstanceFromJson(child)) instance->AddChild(converted);
  }
  return instance;
}

}
