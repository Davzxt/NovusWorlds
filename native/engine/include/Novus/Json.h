#pragma once

#include <map>
#include <string>
#include <variant>
#include <vector>

namespace Novus {

class Json {
public:
  using Object = std::map<std::string, Json>;
  using Array = std::vector<Json>;

  Json();
  Json(std::nullptr_t);
  Json(bool value);
  Json(double value);
  Json(int value);
  Json(const char* value);
  Json(std::string value);
  Json(Object value);
  Json(Array value);

  bool IsNull() const;
  bool IsBool() const;
  bool IsNumber() const;
  bool IsString() const;
  bool IsObject() const;
  bool IsArray() const;

  bool AsBool(bool fallback = false) const;
  double AsNumber(double fallback = 0.0) const;
  const std::string& AsString(const std::string& fallback = EmptyString()) const;
  const Object& AsObject() const;
  const Array& AsArray() const;

  Object& ObjectItems();
  Array& ArrayItems();

  const Json& Get(const std::string& key) const;
  Json& Set(const std::string& key, Json value);
  bool Has(const std::string& key) const;

  std::string Dump(int indent = 0) const;

  static Json Parse(const std::string& text);
  static const Json& Null();
  static const std::string& EmptyString();

private:
  using Value = std::variant<std::nullptr_t, bool, double, std::string, Object, Array>;
  Value value_;
};

}
