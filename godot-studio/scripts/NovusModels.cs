using Godot;
using System.Collections.Generic;

public sealed class NovusMap
{
    public int GameId;
    public string Name = "Novus Place";
    public string Description = "";
    public string ThumbnailUrl = "";
    public readonly List<NovusPart> Objects = new();
    public readonly List<NovusScript> Scripts = new();
    public Vector3 Spawn = new(0, 4, 0);
    public Color SkyColor = new(0.53f, 0.81f, 0.92f);
    public int MaxPlayers = 20;
}

public sealed class NovusPart
{
    public string Id = "";
    public string Type = "Part";
    public string ParentId = "";
    public string Name = "Part";
    public Vector3 Position = Vector3.Zero;
    public Vector3 Rotation = Vector3.Zero;
    public Vector3 Size = new(4, 1, 4);
    public Color Color = Colors.LightGray;
    public string Material = "Plastic";
    public bool Anchored = true;
    public bool CanCollide = true;
    public bool Locked;
    public bool Visible = true;
    public bool CastShadow = true;
    public float Transparency;
    public float Reflectance;
    public float Brightness = 1.2f;
    public float Range = 18f;
}

public sealed class NovusScript
{
    public string Id = "";
    public string Name = "Script";
    public string ParentId = "";
    public string Source = "print(\"Hello from Novus Luau\")";
    public bool Disabled;
}

public sealed class StudioGameSummary
{
    public int Id;
    public string Title = "Sem nome";
    public string Description = "";
    public string UpdatedAt = "";
    public bool IsActive;
}
