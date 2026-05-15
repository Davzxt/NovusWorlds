#pragma once

#include <string>

namespace Novus {

struct HttpResponse {
  int status = 0;
  std::string body;
  std::string error;
};

HttpResponse HttpGet(const std::string& url);
HttpResponse HttpPostJson(const std::string& url, const std::string& json);
std::string ToWebSocketUrl(const std::string& httpBaseUrl, const std::string& path);

}
