#include "Novus/Json.h"

#include <cctype>
#include <iomanip>
#include <sstream>
#include <stdexcept>
#include <utility>

namespace Novus {
namespace {

class Parser {
public:
  explicit Parser(const std::string& text) : text_(text) {}

  Json Parse() {
    Skip();
    Json value = ParseValue();
    Skip();
    if (pos_ != text_.size()) throw std::runtime_error("Unexpected data after JSON value.");
    return value;
  }

private:
  Json ParseValue() {
    Skip();
    if (pos_ >= text_.size()) throw std::runtime_error("Unexpected end of JSON.");
    const char c = text_[pos_];
    if (c == '{') return ParseObject();
    if (c == '[') return ParseArray();
    if (c == '"') return Json(ParseString());
    if (c == '-' || std::isdigit(static_cast<unsigned char>(c))) return Json(ParseNumber());
    if (Match("true")) return Json(true);
    if (Match("false")) return Json(false);
    if (Match("null")) return Json(nullptr);
    throw std::runtime_error("Invalid JSON token.");
  }

  Json ParseObject() {
    Json::Object object;
    Expect('{');
    Skip();
    if (Peek('}')) {
      ++pos_;
      return object;
    }
    while (true) {
      Skip();
      const std::string key = ParseString();
      Skip();
      Expect(':');
      object[key] = ParseValue();
      Skip();
      if (Peek('}')) {
        ++pos_;
        break;
      }
      Expect(',');
    }
    return object;
  }

  Json ParseArray() {
    Json::Array array;
    Expect('[');
    Skip();
    if (Peek(']')) {
      ++pos_;
      return array;
    }
    while (true) {
      array.push_back(ParseValue());
      Skip();
      if (Peek(']')) {
        ++pos_;
        break;
      }
      Expect(',');
    }
    return array;
  }

  std::string ParseString() {
    Expect('"');
    std::string out;
    while (pos_ < text_.size()) {
      const char c = text_[pos_++];
      if (c == '"') return out;
      if (c != '\\') {
        out.push_back(c);
        continue;
      }
      if (pos_ >= text_.size()) throw std::runtime_error("Bad JSON escape.");
      const char e = text_[pos_++];
      switch (e) {
      case '"': out.push_back('"'); break;
      case '\\': out.push_back('\\'); break;
      case '/': out.push_back('/'); break;
      case 'b': out.push_back('\b'); break;
      case 'f': out.push_back('\f'); break;
      case 'n': out.push_back('\n'); break;
      case 'r': out.push_back('\r'); break;
      case 't': out.push_back('\t'); break;
      case 'u':
        if (pos_ + 4 > text_.size()) throw std::runtime_error("Bad JSON unicode escape.");
        out.push_back('?');
        pos_ += 4;
        break;
      default:
        throw std::runtime_error("Bad JSON escape.");
      }
    }
    throw std::runtime_error("Unterminated JSON string.");
  }

  double ParseNumber() {
    const size_t start = pos_;
    if (Peek('-')) ++pos_;
    while (pos_ < text_.size() && std::isdigit(static_cast<unsigned char>(text_[pos_]))) ++pos_;
    if (Peek('.')) {
      ++pos_;
      while (pos_ < text_.size() && std::isdigit(static_cast<unsigned char>(text_[pos_]))) ++pos_;
    }
    if (Peek('e') || Peek('E')) {
      ++pos_;
      if (Peek('+') || Peek('-')) ++pos_;
      while (pos_ < text_.size() && std::isdigit(static_cast<unsigned char>(text_[pos_]))) ++pos_;
    }
    return std::stod(text_.substr(start, pos_ - start));
  }

  bool Match(const char* value) {
    const std::string token(value);
    if (text_.compare(pos_, token.size(), token) != 0) return false;
    pos_ += token.size();
    return true;
  }

  bool Peek(char c) const {
    return pos_ < text_.size() && text_[pos_] == c;
  }

  void Expect(char c) {
    if (!Peek(c)) throw std::runtime_error("Unexpected JSON character.");
    ++pos_;
  }

  void Skip() {
    while (pos_ < text_.size() && std::isspace(static_cast<unsigned char>(text_[pos_]))) ++pos_;
  }

  const std::string& text_;
  size_t pos_ = 0;
};

std::string Escape(const std::string& value) {
  std::ostringstream out;
  for (const char c : value) {
    switch (c) {
    case '"': out << "\\\""; break;
    case '\\': out << "\\\\"; break;
    case '\b': out << "\\b"; break;
    case '\f': out << "\\f"; break;
    case '\n': out << "\\n"; break;
    case '\r': out << "\\r"; break;
    case '\t': out << "\\t"; break;
    default:
      if (static_cast<unsigned char>(c) < 0x20) {
        out << "\\u" << std::hex << std::setw(4) << std::setfill('0') << int(c);
      } else {
        out << c;
      }
      break;
    }
  }
  return out.str();
}

std::string Indent(int count) {
  return std::string(static_cast<size_t>(std::max(0, count)), ' ');
}

}

Json::Json() : value_(nullptr) {}
Json::Json(std::nullptr_t) : value_(nullptr) {}
Json::Json(bool value) : value_(value) {}
Json::Json(double value) : value_(value) {}
Json::Json(int value) : value_(static_cast<double>(value)) {}
Json::Json(const char* value) : value_(std::string(value ? value : "")) {}
Json::Json(std::string value) : value_(std::move(value)) {}
Json::Json(Object value) : value_(std::move(value)) {}
Json::Json(Array value) : value_(std::move(value)) {}

bool Json::IsNull() const { return std::holds_alternative<std::nullptr_t>(value_); }
bool Json::IsBool() const { return std::holds_alternative<bool>(value_); }
bool Json::IsNumber() const { return std::holds_alternative<double>(value_); }
bool Json::IsString() const { return std::holds_alternative<std::string>(value_); }
bool Json::IsObject() const { return std::holds_alternative<Object>(value_); }
bool Json::IsArray() const { return std::holds_alternative<Array>(value_); }

bool Json::AsBool(bool fallback) const {
  return IsBool() ? std::get<bool>(value_) : fallback;
}

double Json::AsNumber(double fallback) const {
  return IsNumber() ? std::get<double>(value_) : fallback;
}

const std::string& Json::AsString(const std::string& fallback) const {
  return IsString() ? std::get<std::string>(value_) : fallback;
}

const Json::Object& Json::AsObject() const {
  static const Object empty;
  return IsObject() ? std::get<Object>(value_) : empty;
}

const Json::Array& Json::AsArray() const {
  static const Array empty;
  return IsArray() ? std::get<Array>(value_) : empty;
}

Json::Object& Json::ObjectItems() {
  if (!IsObject()) value_ = Object{};
  return std::get<Object>(value_);
}

Json::Array& Json::ArrayItems() {
  if (!IsArray()) value_ = Array{};
  return std::get<Array>(value_);
}

const Json& Json::Get(const std::string& key) const {
  if (!IsObject()) return Null();
  const auto& object = std::get<Object>(value_);
  const auto it = object.find(key);
  return it == object.end() ? Null() : it->second;
}

Json& Json::Set(const std::string& key, Json value) {
  ObjectItems()[key] = std::move(value);
  return *this;
}

bool Json::Has(const std::string& key) const {
  return IsObject() && std::get<Object>(value_).find(key) != std::get<Object>(value_).end();
}

std::string Json::Dump(int indent) const {
  if (IsNull()) return "null";
  if (IsBool()) return AsBool() ? "true" : "false";
  if (IsNumber()) {
    std::ostringstream out;
    out << std::setprecision(12) << AsNumber();
    return out.str();
  }
  if (IsString()) return "\"" + Escape(AsString()) + "\"";
  if (IsArray()) {
    const auto& array = AsArray();
    if (array.empty()) return "[]";
    std::ostringstream out;
    out << "[";
    for (size_t i = 0; i < array.size(); ++i) {
      if (i) out << ",";
      if (indent > 0) out << "\n" << Indent(indent);
      out << array[i].Dump(indent > 0 ? indent + 2 : 0);
    }
    if (indent > 0) out << "\n" << Indent(indent - 2);
    out << "]";
    return out.str();
  }
  const auto& object = AsObject();
  if (object.empty()) return "{}";
  std::ostringstream out;
  out << "{";
  bool first = true;
  for (const auto& [key, value] : object) {
    if (first) {
      first = false;
      if (indent > 0) out << "\n" << Indent(indent);
    } else {
      out << (indent > 0 ? ",\n" + Indent(indent) : ",");
    }
    out << "\"" << Escape(key) << "\":" << (indent > 0 ? " " : "") << value.Dump(indent > 0 ? indent + 2 : 0);
  }
  if (indent > 0) out << "\n" << Indent(indent - 2);
  out << "}";
  return out.str();
}

Json Json::Parse(const std::string& text) {
  return Parser(text).Parse();
}

const Json& Json::Null() {
  static const Json nullValue;
  return nullValue;
}

const std::string& Json::EmptyString() {
  static const std::string empty;
  return empty;
}

}
