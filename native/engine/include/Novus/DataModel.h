#pragma once

#include "Novus/Json.h"
#include "Novus/Math.h"

#include <memory>
#include <string>
#include <vector>

namespace Novus {

enum class InstanceType {
  DataModel,
  Service,
  Workspace,
  Players,
  Lighting,
  StarterGui,
  StarterPack,
  ReplicatedStorage,
  ServerScriptService,
  Model,
  Part,
  SpawnLocation,
  Script,
  Tool,
  Decal,
  PointLight,
  SurfaceLight,
  Humanoid
};

std::string ToString(InstanceType type);
InstanceType InstanceTypeFromString(const std::string& value);

struct Transform {
  Vec3 position;
  Vec3 rotation;
  Vec3 size = { 1.0f, 1.0f, 1.0f };
};

struct Instance {
  std::string id;
  std::string name;
  InstanceType type = InstanceType::Part;
  Transform transform;
  Color color = { 0.55f, 0.75f, 0.38f, 1.0f };
  std::string material = "Plastic";
  bool anchored = true;
  bool canCollide = true;
  bool visible = true;
  bool locked = false;
  float transparency = 0.0f;
  float reflectance = 0.0f;
  std::string source;
  std::vector<std::shared_ptr<Instance>> children;

  Instance() = default;
  Instance(InstanceType instanceType, std::string instanceName);

  std::shared_ptr<Instance> AddChild(std::shared_ptr<Instance> child);
  Instance* FindById(const std::string& wantedId);
  const Instance* FindById(const std::string& wantedId) const;
};

class DataModel {
public:
  DataModel();

  std::shared_ptr<Instance> Root() const { return root_; }
  std::shared_ptr<Instance> Workspace() const { return workspace_; }
  std::shared_ptr<Instance> Service(InstanceType type) const;

  int CountInstances() const;
  Json ToJson() const;

  static DataModel CreateEmptyPlace(const std::string& name = "Novo Mundo");
  static DataModel FromLegacyPlaceJson(const Json& value);
  static DataModel FromDataModelJson(const Json& value);

private:
  std::shared_ptr<Instance> root_;
  std::shared_ptr<Instance> workspace_;

  void AddDefaultServices();
};

Json InstanceToJson(const Instance& instance);
std::shared_ptr<Instance> InstanceFromJson(const Json& value);

}
