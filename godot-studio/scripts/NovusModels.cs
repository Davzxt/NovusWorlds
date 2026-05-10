using Godot;
using System.Collections.Generic;

public sealed class NovusMap
{
    public string Name = "Novus Place";
    public readonly List<NovusPart> Objects = new();
    public Vector3 Spawn = new(0, 4, 0);
    public Color SkyColor = new(0.53f, 0.81f, 0.92f);
}

public sealed class NovusPart
{
    public string Id = "";
    public string Type = "Part";
    public string Name = "Part";
    public Vector3 Position = Vector3.Zero;
    public Vector3 Rotation = Vector3.Zero;
    public Vector3 Size = new(4, 1, 4);
    public Color Color = Colors.LightGray;
    public string Material = "Plastic";
    public bool Anchored = true;
    public bool CanCollide = true;
    public float Transparency;
}
