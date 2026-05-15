#include "Novus/HttpClient.h"

#include <sstream>
#include <iterator>
#include <vector>
#include <windows.h>
#include <winhttp.h>

namespace Novus {
namespace {

struct ParsedUrl {
  std::wstring scheme;
  std::wstring host;
  std::wstring path;
  INTERNET_PORT port = 0;
  bool secure = false;
};

std::wstring Wide(const std::string& value) {
  if (value.empty()) return {};
  const int length = MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, nullptr, 0);
  std::wstring out(static_cast<size_t>(length), L'\0');
  MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, out.data(), length);
  out.pop_back();
  return out;
}

std::string Utf8(const std::wstring& value) {
  if (value.empty()) return {};
  const int length = WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, nullptr, 0, nullptr, nullptr);
  std::string out(static_cast<size_t>(length), '\0');
  WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, out.data(), length, nullptr, nullptr);
  out.pop_back();
  return out;
}

ParsedUrl ParseUrl(const std::string& url) {
  URL_COMPONENTS parts{};
  parts.dwStructSize = sizeof(parts);
  wchar_t host[256]{};
  wchar_t path[4096]{};
  wchar_t extra[2048]{};
  wchar_t scheme[16]{};
  parts.lpszHostName = host;
  parts.dwHostNameLength = static_cast<DWORD>(std::size(host));
  parts.lpszUrlPath = path;
  parts.dwUrlPathLength = static_cast<DWORD>(std::size(path));
  parts.lpszExtraInfo = extra;
  parts.dwExtraInfoLength = static_cast<DWORD>(std::size(extra));
  parts.lpszScheme = scheme;
  parts.dwSchemeLength = static_cast<DWORD>(std::size(scheme));

  const std::wstring wideUrl = Wide(url);
  if (!WinHttpCrackUrl(wideUrl.c_str(), 0, 0, &parts)) return {};

  ParsedUrl parsed;
  parsed.scheme.assign(parts.lpszScheme, parts.dwSchemeLength);
  parsed.host.assign(parts.lpszHostName, parts.dwHostNameLength);
  parsed.path.assign(parts.lpszUrlPath, parts.dwUrlPathLength);
  parsed.path.append(parts.lpszExtraInfo, parts.dwExtraInfoLength);
  if (parsed.path.empty()) parsed.path = L"/";
  parsed.port = parts.nPort;
  parsed.secure = parts.nScheme == INTERNET_SCHEME_HTTPS;
  return parsed;
}

HttpResponse Request(const std::string& method, const std::string& url, const std::string& body) {
  HttpResponse response;
  const ParsedUrl parsed = ParseUrl(url);
  if (parsed.host.empty()) {
    response.error = "URL invalida.";
    return response;
  }

  HINTERNET session = WinHttpOpen(L"NovusWorldsNative/1.0", WINHTTP_ACCESS_TYPE_DEFAULT_PROXY, WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
  if (!session) {
    response.error = "WinHttpOpen falhou.";
    return response;
  }

  HINTERNET connect = WinHttpConnect(session, parsed.host.c_str(), parsed.port, 0);
  if (!connect) {
    response.error = "WinHttpConnect falhou.";
    WinHttpCloseHandle(session);
    return response;
  }

  const std::wstring wideMethod = Wide(method);
  HINTERNET request = WinHttpOpenRequest(
    connect,
    wideMethod.c_str(),
    parsed.path.c_str(),
    nullptr,
    WINHTTP_NO_REFERER,
    WINHTTP_DEFAULT_ACCEPT_TYPES,
    parsed.secure ? WINHTTP_FLAG_SECURE : 0);

  if (!request) {
    response.error = "WinHttpOpenRequest falhou.";
    WinHttpCloseHandle(connect);
    WinHttpCloseHandle(session);
    return response;
  }

  std::wstring headers = L"Accept: application/json\r\n";
  if (!body.empty()) headers += L"Content-Type: application/json\r\n";
  const BOOL sent = WinHttpSendRequest(
    request,
    headers.c_str(),
    static_cast<DWORD>(headers.size()),
    body.empty() ? WINHTTP_NO_REQUEST_DATA : (LPVOID)body.data(),
    static_cast<DWORD>(body.size()),
    static_cast<DWORD>(body.size()),
    0);
  if (!sent || !WinHttpReceiveResponse(request, nullptr)) {
    response.error = "Request HTTP falhou.";
    WinHttpCloseHandle(request);
    WinHttpCloseHandle(connect);
    WinHttpCloseHandle(session);
    return response;
  }

  DWORD status = 0;
  DWORD statusSize = sizeof(status);
  WinHttpQueryHeaders(request, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER, nullptr, &status, &statusSize, nullptr);
  response.status = static_cast<int>(status);

  DWORD available = 0;
  do {
    if (!WinHttpQueryDataAvailable(request, &available) || available == 0) break;
    std::vector<char> buffer(available);
    DWORD read = 0;
    if (!WinHttpReadData(request, buffer.data(), available, &read) || read == 0) break;
    response.body.append(buffer.data(), read);
  } while (available > 0);

  WinHttpCloseHandle(request);
  WinHttpCloseHandle(connect);
  WinHttpCloseHandle(session);
  return response;
}

}

HttpResponse HttpGet(const std::string& url) {
  return Request("GET", url, "");
}

HttpResponse HttpPostJson(const std::string& url, const std::string& json) {
  return Request("POST", url, json);
}

std::string ToWebSocketUrl(const std::string& httpBaseUrl, const std::string& path) {
  std::string value = httpBaseUrl;
  if (value.rfind("https://", 0) == 0) value.replace(0, 8, "wss://");
  else if (value.rfind("http://", 0) == 0) value.replace(0, 7, "ws://");
  while (!value.empty() && value.back() == '/') value.pop_back();
  return value + path;
}

}
