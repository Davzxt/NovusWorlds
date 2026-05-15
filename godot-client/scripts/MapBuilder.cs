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
        map.Objects.Add(new NovusPart { Id = "road-x", Name = "Road X", Position = new Vector3(0, 0.04f, 0), Size = new Vector3(116, 0.12f, 12), Color = new Color(0.36f, 0.36f, 0.38f), Material = "Metal" });
        map.Objects.Add(new NovusPart { Id = "road-z", Name = "Road Z", Position = new Vector3(0, 0.05f, 0), Size = new Vector3(12, 0.13f, 116), Color = new Color(0.36f, 0.36f, 0.38f), Material = "Metal" });
        for (var i = -5; i <= 5; i++)
        {
            map.Objects.Add(new NovusPart { Id = $"stripe-x-{i}", Name = "Road Stripe", Position = new Vector3(i * 10, 0.14f, 0), Size = new Vector3(4.4f, 0.08f, 0.8f), Color = Colors.White, Material = "Plastic" });
            map.Objects.Add(new NovusPart { Id = $"stripe-z-{i}", Name = "Road Stripe", Position = new Vector3(0, 0.15f, i * 10), Size = new Vector3(0.8f, 0.08f, 4.4f), Color = Colors.White, Material = "Plastic" });
        }
        map.Objects.Add(new NovusPart { Id = "red-spawn-pad", Name = "Spawn Pad", Position = new Vector3(0, 0.3f, 18), Size = new Vector3(8, 0.6f, 8), Color = new Color(0.9f, 0.12f, 0.08f), Material = "Brick" });
        map.Objects.Add(new NovusPart { Id = "blue-water", Name = "Classic Water", Position = new Vector3(-34, 0.06f, -32), Size = new Vector3(24, 0.12f, 18), Color = new Color(0.05f, 0.38f, 0.75f, 0.82f), Material = "Glass", Transparency = 0.22f });
        map.Objects.Add(new NovusPart { Id = "brown-house", Name = "Brown Block House", Position = new Vector3(28, 3, -24), Size = new Vector3(14, 6, 12), Color = new Color(0.43f, 0.23f, 0.12f), Material = "Wood" });
        map.Objects.Add(new NovusPart { Id = "brown-house-roof", Name = "House Roof", Position = new Vector3(28, 7.4f, -24), Size = new Vector3(16, 2, 14), Color = new Color(0.23f, 0.2f, 0.18f), Material = "Wood" });
        map.Objects.Add(new NovusPart { Id = "tower-a", Name = "Stud Tower A", Position = new Vector3(-36, 12, 26), Size = new Vector3(10, 24, 10), Color = new Color(0.55f, 0.57f, 0.55f), Material = "Stone" });
        map.Objects.Add(new NovusPart { Id = "tower-b", Name = "Stud Tower B", Position = new Vector3(-22, 17, 30), Size = new Vector3(9, 34, 9), Color = new Color(0.62f, 0.64f, 0.62f), Material = "Stone" });
        map.Objects.Add(new NovusPart { Id = "jump-pad", Name = "Jump Pad", Position = new Vector3(20, 0.25f, 18), Size = new Vector3(8, 0.5f, 8), Color = new Color(1f, 0.86f, 0.08f), Material = "Plastic" });
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
        var tileStuds = 1f;
        return new Vector2(
            Mathf.Max(1f, part.Size.X / tileStuds),
            Mathf.Max(1f, part.Size.Z / tileStuds)
        );
    }
}
