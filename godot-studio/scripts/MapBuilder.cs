using Godot;

public static class MapBuilder
{
    public static Node3D Build(NovusMap map)
    {
        var root = new Node3D { Name = "Workspace" };
        foreach (var part in map.Objects)
        {
            Node3D body = part.Anchored ? new StaticBody3D() : new RigidBody3D();
            body.Name = part.Name;
            body.Position = part.Position;
            body.RotationDegrees = part.Rotation;
            body.SetMeta("novus_id", part.Id);
            body.SetMeta("novus_type", "part");

            var mesh = new MeshInstance3D();
            mesh.Mesh = MeshFor(part);
            var material = new StandardMaterial3D
            {
                AlbedoColor = new Color(part.Color.R, part.Color.G, part.Color.B, 1f - part.Transparency),
                Roughness = part.Material == "Metal" ? 0.25f : 0.8f,
                Metallic = part.Material == "Metal" ? 0.8f : 0f,
                EmissionEnabled = part.Name.Contains("Spawn", System.StringComparison.OrdinalIgnoreCase),
                Emission = part.Name.Contains("Spawn", System.StringComparison.OrdinalIgnoreCase) ? part.Color : Colors.Black,
                EmissionEnergyMultiplier = 0.35f
            };
            if (part.Reflectance > 0) material.Roughness = Mathf.Clamp(1f - part.Reflectance, 0.05f, 1f);
            if (part.Transparency > 0) material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            mesh.MaterialOverride = material;
            body.AddChild(mesh);

            if (part.CanCollide)
            {
                var collision = new CollisionShape3D();
                collision.Shape = CollisionFor(part);
                body.AddChild(collision);
            }
            root.AddChild(body);
        }
        return root;
    }

    private static Mesh MeshFor(NovusPart part)
    {
        if (part.Type == "Sphere") return new SphereMesh { Radius = Mathf.Max(Mathf.Max(part.Size.X, part.Size.Y), part.Size.Z) / 2f, Height = part.Size.Y };
        if (part.Type == "Cylinder") return new CylinderMesh { TopRadius = part.Size.X / 2f, BottomRadius = part.Size.X / 2f, Height = part.Size.Y };
        if (part.Type == "Wedge") return CreateWedge(part.Size);
        return new BoxMesh { Size = part.Size };
    }

    private static Shape3D CollisionFor(NovusPart part)
    {
        if (part.Type == "Sphere") return new SphereShape3D { Radius = Mathf.Max(Mathf.Max(part.Size.X, part.Size.Y), part.Size.Z) / 2f };
        if (part.Type == "Cylinder") return new CylinderShape3D { Radius = part.Size.X / 2f, Height = part.Size.Y };
        return new BoxShape3D { Size = part.Size };
    }

    private static ArrayMesh CreateWedge(Vector3 size)
    {
        var hx = size.X / 2f;
        var hy = size.Y / 2f;
        var hz = size.Z / 2f;
        var verts = new[]
        {
            new Vector3(-hx, -hy, -hz), new Vector3(hx, -hy, -hz), new Vector3(-hx, -hy, hz), new Vector3(hx, -hy, hz),
            new Vector3(-hx, hy, hz), new Vector3(hx, hy, hz)
        };
        var faces = new[]
        {
            0, 1, 3, 0, 3, 2,
            2, 3, 5, 2, 5, 4,
            0, 2, 4, 0, 4, 1,
            1, 4, 5, 1, 5, 3,
            0, 1, 3, 0, 3, 2
        };
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var index in faces) st.AddVertex(verts[index]);
        st.GenerateNormals();
        return st.Commit() as ArrayMesh ?? new ArrayMesh();
    }
}
