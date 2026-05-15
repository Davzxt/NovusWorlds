using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace NovusWorldsProtocolLauncher
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                var root = AppDomain.CurrentDomain.BaseDirectory;
                var config = LoadConfig(Path.Combine(root, "config.json"));
                var cacheDir = Expand(ConfigGet(config, "cacheDir", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovusWorlds", "Cache")));
                Directory.CreateDirectory(cacheDir);
                var logPath = Path.Combine(cacheDir, "launcher.log");

                if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
                {
                    MessageBox.Show("Abra um jogo pelo site Novus Worlds. O launcher precisa de um ticket novus://.", "Novus Worlds", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                Log(logPath, "Protocol: " + args[0]);
                var uri = new Uri(args[0]);
                var query = ParseQuery(uri.Query);

                if (uri.Scheme.Equals("novus", StringComparison.OrdinalIgnoreCase))
                {
                    var ticket = Need(query, "ticket");
                    var gameId = Need(query, "gameId");
                    var baseUrl = NormalizeBaseUrl(Need(query, "baseUrl"));
                    var serverHost = query.ContainsKey("server") ? query["server"] : ConfigGet(config, "realtimeHost", "127.0.0.1");
                    var serverPort = query.ContainsKey("port") ? query["port"] : ConfigGet(config, "realtimePort", "53640");
                    var joinJson = DownloadText(baseUrl.TrimEnd('/') + "/api/legacy/tickets/" + Uri.EscapeDataString(ticket));
                    var joinPath = WriteCache(cacheDir, "join-" + gameId + ".json", joinJson);
                    var playerExe = ResolveExe(ConfigGet(config, "playerExe", ""), "NovusWorldsClient.exe");
                    StartApp(logPath, playerExe, new[] { "--game", gameId, "--base-url", baseUrl, "--server", serverHost, "--port", serverPort, "--ticket", ticket, "--join-json", joinPath });
                    return 0;
                }

                if (uri.Scheme.Equals("novus-studio", StringComparison.OrdinalIgnoreCase))
                {
                    var ticket = Need(query, "ticket");
                    var baseUrl = NormalizeBaseUrl(Need(query, "baseUrl"));
                    var projectJson = DownloadText(baseUrl.TrimEnd('/') + "/api/legacy/studio-project?ticket=" + Uri.EscapeDataString(ticket));
                    var projectPath = WriteCache(cacheDir, "studio-project.json", projectJson);
                    var studioExe = ResolveExe(ConfigGet(config, "studioExe", ""), "NovusWorldsStudio.exe");
                    StartApp(logPath, studioExe, new[] { "--base-url", baseUrl, "--ticket", ticket, "--project-json", projectPath });
                    return 0;
                }

                throw new InvalidOperationException("Protocolo nao suportado: " + uri.Scheme);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Novus Worlds Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static Dictionary<string, string> LoadConfig(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return values;
            var text = File.ReadAllText(path);
            foreach (Match match in Regex.Matches(text, "\"(?<key>[A-Za-z0-9_]+)\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\""))
            {
                values[match.Groups["key"].Value] = Regex.Unescape(match.Groups["value"].Value);
            }
            foreach (Match match in Regex.Matches(text, "\"(?<key>[A-Za-z0-9_]+)\"\\s*:\\s*(?<value>\\d+)"))
            {
                if (!values.ContainsKey(match.Groups["key"].Value)) values[match.Groups["key"].Value] = match.Groups["value"].Value;
            }
            return values;
        }

        private static string ConfigGet(Dictionary<string, string> values, string key, string fallback)
        {
            return values.ContainsKey(key) && !string.IsNullOrWhiteSpace(values[key]) ? values[key] : fallback;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var trimmed = (query ?? "").TrimStart('?');
            if (string.IsNullOrWhiteSpace(trimmed)) return values;
            foreach (var pair in trimmed.Split('&'))
            {
                if (string.IsNullOrWhiteSpace(pair)) continue;
                var pieces = pair.Split(new[] { '=' }, 2);
                var key = Uri.UnescapeDataString(pieces[0].Replace("+", " "));
                var value = pieces.Length > 1 ? Uri.UnescapeDataString(pieces[1].Replace("+", " ")) : "";
                values[key] = value;
            }
            return values;
        }

        private static string Need(Dictionary<string, string> values, string key)
        {
            if (!values.ContainsKey(key) || string.IsNullOrWhiteSpace(values[key])) throw new InvalidOperationException("Ticket invalido: parametro ausente '" + key + "'.");
            return values[key];
        }

        private static string DownloadText(string url)
        {
            using (var web = new WebClient())
            {
                web.Headers.Add("User-Agent", "NovusWorldsLauncher/1.0");
                return web.DownloadString(url);
            }
        }

        private static string WriteCache(string cacheDir, string name, string text)
        {
            var safe = Regex.Replace(name, "[^A-Za-z0-9_.-]", "_");
            var path = Path.Combine(cacheDir, safe);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return path;
        }

        private static string ResolveExe(string value, string exeName)
        {
            var raw = Expand(value);
            if (File.Exists(raw)) return raw;
            if (Directory.Exists(raw))
            {
                var hits = Directory.GetFiles(raw, exeName, SearchOption.AllDirectories);
                if (hits.Length > 0) return hits[0];
            }
            throw new FileNotFoundException(exeName + " nao encontrado. Rode o instalador novamente.", raw);
        }

        private static void StartApp(string logPath, string exe, string[] args)
        {
            Log(logPath, "Launching: " + exe + " " + string.Join(" ", args));
            var info = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = false,
                Arguments = JoinArguments(args)
            };
            Process.Start(info);
        }

        private static string JoinArguments(IEnumerable<string> args)
        {
            var output = new StringBuilder();
            foreach (var arg in args)
            {
                if (output.Length > 0) output.Append(' ');
                output.Append('"').Append((arg ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
            }
            return output.ToString();
        }

        private static string NormalizeBaseUrl(string value)
        {
            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && value.IndexOf(".onrender.com", StringComparison.OrdinalIgnoreCase) > 0)
            {
                return "https://" + value.Substring("http://".Length);
            }
            return value;
        }

        private static string Expand(string value)
        {
            return Environment.ExpandEnvironmentVariables(value ?? "");
        }

        private static void Log(string path, string message)
        {
            try { File.AppendAllText(path, "[" + DateTime.Now.ToString("s") + "] " + message + Environment.NewLine); } catch { }
        }
    }
}
