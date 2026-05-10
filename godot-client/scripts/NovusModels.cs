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

public sealed class NovusAvatar
{
    public string Username = "NovusPlayer";
    public Color HeadColor = new(0.96f, 0.8f, 0.19f);
    public Color TorsoColor = new(0.05f, 0.41f, 0.67f);
    public Color ArmsColor = new(0.96f, 0.8f, 0.19f);
    public Color LegsColor = new(0.1f, 0.16f, 0.21f);
    public readonly List<NovusAvatarItem> Items = new();
}

public sealed class NovusAvatarItem
{
    public int Id;
    public string Name = "Item";
    public string Type = "";
    public string ModelUrl = "";
    public string TextureUrl = "";
    public Vector3 HatPosition = new(0, 1.2f, 0);
    public Vector3 HatRotation = Vector3.Zero;
    public Vector3 HatScale = Vector3.One;
}
