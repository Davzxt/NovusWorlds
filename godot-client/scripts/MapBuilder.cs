using Godot;

public static class MapBuilder
{
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
            var material = new StandardMaterial3D
            {
                AlbedoColor = new Color(displayColor.R, displayColor.G, displayColor.B, 1f - part.Transparency),
                Roughness = part.Material == "Metal" ? 0.25f : 0.8f,
                Metallic = part.Material == "Metal" ? 0.8f : 0f
            };
            if (part.Transparency > 0) material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            mesh.MaterialOverride = material;
            body.AddChild(mesh);

            if (part.CanCollide)
            {
                var collision = new CollisionShape3D();
                collision.Shape = new BoxShape3D { Size = part.Size };
                body.AddChild(collision);
            }
            root.AddChild(body);
            if (part.Size.X >= 12 && part.Size.Z >= 12 && part.Transparency <= 0.05f)
                AddStuds(body, part, displayColor);
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

    private static void AddStuds(Node3D body, NovusPart part, Color displayColor)
    {
        var sx = Mathf.Min(28, Mathf.Max(2, Mathf.FloorToInt(part.Size.X / 4f)));
        var sz = Mathf.Min(28, Mathf.Max(2, Mathf.FloorToInt(part.Size.Z / 4f)));
        var mat = new StandardMaterial3D { AlbedoColor = displayColor.Lightened(0.18f), Roughness = 0.82f };
        for (var x = 0; x < sx; x++)
        for (var z = 0; z < sz; z++)
        {
            var stud = new MeshInstance3D
            {
                Name = "Stud",
                Mesh = new CylinderMesh { TopRadius = 0.42f, BottomRadius = 0.42f, Height = 0.12f, RadialSegments = 16 },
                Position = new Vector3((x - (sx - 1) / 2f) * 4f, part.Size.Y / 2f + 0.07f, (z - (sz - 1) / 2f) * 4f),
                MaterialOverride = mat
            };
            body.AddChild(stud);
        }
    }
}
