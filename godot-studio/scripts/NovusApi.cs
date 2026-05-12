using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;

public static class NovusApi
{
    public static async Task<NovusMap> LoadPlace(string baseUrl, string gameId)
    {
        var http = new HttpRequest();
        AddChildTemp(http);
        var err = http.Request($"{baseUrl.TrimEnd('/')}/api/legacy/place/{Uri.EscapeDataString(gameId)}");
        if (err != Error.Ok) throw new Exception($"HTTP request failed: {err}");
        var result = await WaitForRequest(http);
        if (result.ResponseCode >= 400) throw new Exception($"API returned {result.ResponseCode}");
        var json = result.Body.GetStringFromUtf8();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var mapJson = root.TryGetProperty("map", out var map) ? map : root;
        return ParseMap(mapJson, root.TryGetProperty("title", out var title) ? title.GetString() ?? "Novus Place" : "Novus Place");
    }

    public static async Task<NovusMap> LoadStudioProject(string baseUrl, string ticket)
    {
        var http = new HttpRequest();
        AddChildTemp(http);
        var err = http.Request($"{baseUrl.TrimEnd('/')}/api/legacy/studio-project?ticket={Uri.EscapeDataString(ticket)}");
        if (err != Error.Ok) throw new Exception($"HTTP request failed: {err}");
        var result = await WaitForRequest(http);
        if (result.ResponseCode >= 400) throw new Exception($"API returned {result.ResponseCode}");
        var json = result.Body.GetStringFromUtf8();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var mapJson = root.TryGetProperty("map", out var map) ? map : root;
        var parsed = ParseMap(mapJson, root.TryGetProperty("title", out var title) ? title.GetString() ?? "Novo Mundo" : "Novo Mundo");
        parsed.GameId = root.TryGetProperty("gameId", out var gameId) && gameId.TryGetInt32(out var id) ? id : 0;
        parsed.Description = root.TryGetProperty("description", out var description) ? description.GetString() ?? "" : "";
        return parsed;
    }

    public static async Task<int> SaveStudioProject(string baseUrl, string ticket, NovusMap map, bool publish)
    {
        var http = new HttpRequest();
        AddChildTemp(http);
        var body = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["ticket"] = ticket,
            ["gameId"] = map.GameId == 0 ? null : map.GameId,
            ["title"] = map.Name,
            ["description"] = map.Description,
            ["publish"] = publish,
            ["map_data"] = ToWireMap(map)
        });
        var err = http.Request(
            $"{baseUrl.TrimEnd('/')}/api/legacy/studio-project/save",
            new[] { "Content-Type: application/json" },
            HttpClient.Method.Post,
            body
        );
        if (err != Error.Ok) throw new Exception($"HTTP request failed: {err}");
        var result = await WaitForRequest(http);
        if (result.ResponseCode >= 400) throw new Exception(result.Body.GetStringFromUtf8());
        using var doc = JsonDocument.Parse(result.Body.GetStringFromUtf8());
        return doc.RootElement.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var id) ? id : map.GameId;
    }

    private static void AddChildTemp(Node node)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        tree?.Root.AddChild(node);
    }

    private static Task<HttpResponse> WaitForRequest(HttpRequest http)
    {
        var tcs = new TaskCompletionSource<HttpResponse>();
        http.RequestCompleted += (result, responseCode, headers, body) =>
        {
            http.QueueFree();
            if (result != (long)HttpRequest.Result.Success) tcs.TrySetException(new Exception($"HTTP result {result}"));
            else tcs.TrySetResult(new HttpResponse { ResponseCode = responseCode, Body = body });
        };
        return tcs.Task;
    }

    public static NovusMap ParseMap(JsonElement mapJson, string title)
    {
        var map = new NovusMap { Name = title };
        if (mapJson.TryGetProperty("name", out var name)) map.Name = name.GetString() ?? title;
        if (mapJson.TryGetProperty("skyColor", out var sky)) map.SkyColor = ParseColor(sky.GetString(), map.SkyColor);
        if (mapJson.TryGetProperty("spawnPoints", out var spawns) && spawns.ValueKind == JsonValueKind.Array && spawns.GetArrayLength() > 0)
        {
            var spawn = spawns[0];
            map.Spawn = new Vector3(GetFloat(spawn, "x", 0), GetFloat(spawn, "y", 4), GetFloat(spawn, "z", 0));
        }
        if (mapJson.TryGetProperty("objects", out var objects) && objects.ValueKind == JsonValueKind.Array)
        {
            foreach (var obj in objects.EnumerateArray())
            {
                var part = new NovusPart
                {
                    Id = GetString(obj, "id", Guid.NewGuid().ToString("N")),
                    Type = GetString(obj, "type", "Part"),
                    Name = GetString(obj, "name", "Part"),
                    ParentId = GetString(obj, "parentId", ""),
                    Color = ParseColor(GetString(obj, "color", "#cccccc"), Colors.LightGray),
                    Material = GetString(obj, "material", "Plastic"),
                    Anchored = GetBool(obj, "anchored", true),
                    CanCollide = GetBool(obj, "canCollide", true),
                    Locked = GetBool(obj, "locked", false),
                    Transparency = GetFloat(obj, "transparency", 0),
                    Reflectance = GetFloat(obj, "reflectance", 0)
                };
                if (obj.TryGetProperty("position", out var pos)) part.Position = new Vector3(GetFloat(pos, "x", 0), GetFloat(pos, "y", 0), GetFloat(pos, "z", 0));
                if (obj.TryGetProperty("rotation", out var rot)) part.Rotation = new Vector3(GetFloat(rot, "x", 0), GetFloat(rot, "y", 0), GetFloat(rot, "z", 0));
                if (obj.TryGetProperty("size", out var size)) part.Size = new Vector3(GetFloat(size, "x", 4), GetFloat(size, "y", 1), GetFloat(size, "z", 4));
                map.Objects.Add(part);
            }
        }
        if (mapJson.TryGetProperty("scripts", out var scripts) && scripts.ValueKind == JsonValueKind.Array)
        {
            foreach (var script in scripts.EnumerateArray())
            {
                map.Scripts.Add(new NovusScript
                {
                    Id = GetString(script, "id", Guid.NewGuid().ToString("N")),
                    Name = GetString(script, "name", "Script"),
                    ParentId = GetString(script, "parentId", ""),
                    Source = GetString(script, "source", ""),
                    Disabled = GetBool(script, "disabled", false)
                });
            }
        }
        EnsurePlayable(map);
        return map;
    }

    public static void EnsurePlayable(NovusMap map)
    {
        foreach (var part in map.Objects)
            if (part.Name.Equals("Baseplate", StringComparison.OrdinalIgnoreCase)) return;
        map.Objects.Insert(0, new NovusPart
        {
            Id = "baseplate",
            Name = "Baseplate",
            Position = new Vector3(0, -0.5f, 0),
            Size = new Vector3(128, 1, 128),
            Color = new Color(0.42f, 0.56f, 0.14f),
            Material = "Grass",
            Anchored = true,
            CanCollide = true
        });
    }

    public static Dictionary<string, object> ToWireMap(NovusMap map)
    {
        var objects = new List<Dictionary<string, object>>();
        foreach (var part in map.Objects)
        {
            objects.Add(new Dictionary<string, object>
            {
                ["id"] = part.Id,
                ["type"] = part.Type,
                ["parentId"] = part.ParentId,
                ["name"] = part.Name,
                ["position"] = new Dictionary<string, object> { ["x"] = part.Position.X, ["y"] = part.Position.Y, ["z"] = part.Position.Z },
                ["rotation"] = new Dictionary<string, object> { ["x"] = part.Rotation.X, ["y"] = part.Rotation.Y, ["z"] = part.Rotation.Z },
                ["size"] = new Dictionary<string, object> { ["x"] = part.Size.X, ["y"] = part.Size.Y, ["z"] = part.Size.Z },
                ["color"] = ToHex(part.Color),
                ["material"] = part.Material,
                ["anchored"] = part.Anchored,
                ["canCollide"] = part.CanCollide,
                ["locked"] = part.Locked,
                ["transparency"] = part.Transparency,
                ["reflectance"] = part.Reflectance,
                ["children"] = Array.Empty<object>()
            });
        }
        var scripts = new List<Dictionary<string, object>>();
        foreach (var script in map.Scripts)
        {
            scripts.Add(new Dictionary<string, object>
            {
                ["id"] = script.Id,
                ["name"] = script.Name,
                ["parentId"] = script.ParentId,
                ["source"] = script.Source,
                ["disabled"] = script.Disabled
            });
        }
        return new Dictionary<string, object>
        {
            ["name"] = map.Name,
            ["version"] = 1,
            ["objects"] = objects,
            ["scripts"] = scripts,
            ["spawnPoints"] = new object[] { new Dictionary<string, object> { ["x"] = map.Spawn.X, ["y"] = map.Spawn.Y, ["z"] = map.Spawn.Z } },
            ["ambient"] = "#404040",
            ["skyColor"] = ToHex(map.SkyColor)
        };
    }

    private static string GetString(JsonElement obj, string key, string fallback) => obj.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static bool GetBool(JsonElement obj, string key, bool fallback) => obj.TryGetProperty(key, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False) ? value.GetBoolean() : fallback;
    private static float GetFloat(JsonElement obj, string key, float fallback) => obj.TryGetProperty(key, out var value) && value.TryGetSingle(out var number) ? number : fallback;

    private static Color ParseColor(string hex, Color fallback)
    {
        hex = (hex ?? "").Trim().TrimStart('#');
        if (hex.Length == 3) hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var n))
            return new Color(((n >> 16) & 255) / 255f, ((n >> 8) & 255) / 255f, (n & 255) / 255f);
        return fallback;
    }

    private static string ToHex(Color color)
    {
        return $"#{(int)(color.R * 255):X2}{(int)(color.G * 255):X2}{(int)(color.B * 255):X2}";
    }

    private sealed class HttpResponse
    {
        public long ResponseCode;
        public byte[] Body = Array.Empty<byte>();
    }
}
