using Godot;

public static class MapBuilder
{
    private static readonly Texture2D[] SurfaceTextures =
    {
        GD.Load<Texture2D>("res://assets/environment/surface_0.png"),
        GD.Load<Texture2D>("res://assets/environment/surface_1.png"),
        GD.Load<Texture2D>("res://assets/environment/surface_2.png"),
        GD.Load<Texture2D>("res://assets/environment/surface_3.png"),
        GD.Load<Texture2D>("res://assets/environment/surface_4.png"),
        GD.Load<Texture2D>("res://assets/environment/surface_5.png"),
        GD.Load<Texture2D>("res://assets/environment/surface_6.png"),
        GD.Load<Texture2D>("res://assets/environment/surface_7.png")
    };

    public static Node3D Build(NovusMap map)
    {
        var root = new Node3D { Name = "Workspace" };
        if (map.Objects.Count == 1 && map.Objects[0].Name.Equals("Baseplate", System.StringComparison.OrdinalIgnoreCase))
            AddClassicTestParts(map);
        foreach (var part in map.Objects)
        {
            Node3D body = part.Anchored ? new StaticBody3D() : new RigidBody3D();
            body.Name = part.Name;
            body.Position = part.Position;
            body.RotationDegrees = part.Rotation;

            var mesh = new MeshInstance3D();
            mesh.Mesh = part.Type == "Sphere" ? new SphereMesh { Radius = Mathf.Max(Mathf.Max(part.Size.X, part.Size.Y), part.Size.Z) / 2f } : new BoxMesh { Size = part.Size };
            var displayColor = part.Name.Equals("Baseplate", System.StringComparison.OrdinalIgnoreCase)
                ? new Color(0.25f, 0.45f, 0.25f)
                : part.Color;
            mesh.MaterialOverride = ClassicPlastic.Material(displayColor, SurfaceFor(part), 1f - part.Transparency, TextureScaleFor(part));
            body.AddChild(mesh);

            if (part.CanCollide)
            {
                var collision = new CollisionShape3D();
                collision.Shape = new BoxShape3D { Size = part.Size };
                body.AddChild(collision);
            }
            root.AddChild(body);
        }
        return root;
    }

    private static void AddClassicTestParts(NovusMap map)
    {
        map.Objects.Add(new NovusPart { Id = "brick-red-wall", Name = "Brick Wall", Position = new Vector3(16, 2, -12), Size = new Vector3(14, 4, 2), Color = new Color(0.75f, 0.12f, 0.08f), Material = "Plastic" });
        map.Objects.Add(new NovusPart { Id = "blue-block", Name = "Blue Block", Position = new Vector3(-12, 2, 8), Size = new Vector3(6, 4, 6), Color = new Color(0.05f, 0.25f, 0.8f), Material = "Plastic" });
        map.Objects.Add(new NovusPart { Id = "yellow-jump", Name = "Jump Pad", Position = new Vector3(0, 0.15f, 14), Size = new Vector3(8, 0.3f, 8), Color = new Color(1f, 0.83f, 0.12f), Material = "Plastic" });
        map.Objects.Add(new NovusPart { Id = "stairs-1", Name = "Classic Step 1", Position = new Vector3(24, 0.5f, 12), Size = new Vector3(6, 1, 6), Color = new Color(0.45f, 0.25f, 0.12f), Material = "Plastic" });
        map.Objects.Add(new NovusPart { Id = "stairs-2", Name = "Classic Step 2", Position = new Vector3(24, 1.5f, 18), Size = new Vector3(6, 1, 6), Color = new Color(0.45f, 0.25f, 0.12f), Material = "Plastic" });
        map.Objects.Add(new NovusPart { Id = "stairs-3", Name = "Classic Step 3", Position = new Vector3(24, 2.5f, 24), Size = new Vector3(6, 1, 6), Color = new Color(0.45f, 0.25f, 0.12f), Material = "Plastic" });
    }

    private static void AddSurfaceTiles(Node3D body, NovusPart part, Color displayColor)
    {
        var texture = SurfaceFor(part);
        if (texture == null) return;
        var tileSize = 10f;
        var sx = Mathf.Min(18, Mathf.Max(1, Mathf.CeilToInt(part.Size.X / tileSize)));
        var sz = Mathf.Min(18, Mathf.Max(1, Mathf.CeilToInt(part.Size.Z / tileSize)));
        var width = part.Size.X / sx;
        var depth = part.Size.Z / sz;
        var mat = ClassicPlastic.Material(
            part.Name.Equals("Baseplate", System.StringComparison.OrdinalIgnoreCase) ? new Color(0.34f, 0.72f, 0.38f) : displayColor.Lightened(0.08f),
            texture
        );
        for (var x = 0; x < sx; x++)
        for (var z = 0; z < sz; z++)
        {
            var tile = new MeshInstance3D
            {
                Name = "StudTile",
                Mesh = new PlaneMesh { Size = new Vector2(width, depth) },
                Position = new Vector3((x - (sx - 1) / 2f) * width, part.Size.Y / 2f + 0.011f, (z - (sz - 1) / 2f) * depth),
                MaterialOverride = mat
            };
            body.AddChild(tile);
        }
    }

    private static Texture2D SurfaceFor(NovusPart part)
    {
        var material = (part.Material ?? "").ToLowerInvariant();
        var name = (part.Name ?? "").ToLowerInvariant();
        if (name.Contains("baseplate") || material.Contains("grass")) return SurfaceTextures[0];
        if (material.Contains("metal")) return SurfaceTextures[2];
        if (material.Contains("brick") || material.Contains("stone")) return SurfaceTextures[1];
        if (material.Contains("wood")) return SurfaceTextures[4];
        return SurfaceTextures[0];
    }

    private static Vector2 TextureScaleFor(NovusPart part)
    {
        var tileStuds = part.Name.Equals("Baseplate", System.StringComparison.OrdinalIgnoreCase) ? 4f : 3f;
        return new Vector2(
            Mathf.Max(1f, part.Size.X / tileStuds),
            Mathf.Max(1f, part.Size.Z / tileStuds)
        );
    }
}
