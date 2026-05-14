using Godot;

public static class MapBuilder
{
    private static readonly string[] SurfaceOrder = { "Plastic", "Metal", "Wood", "Stone", "Grass", "Brick", "Glass", "Ice" };
    private static readonly Texture2D?[] SurfaceTextures = new Texture2D?[8];
    private static bool surfacesLoaded;

    public static Node3D Build(NovusMap map)
    {
        var root = new Node3D { Name = "Workspace" };
        foreach (var part in map.Objects)
        {
            if (!part.Visible) continue;
            if (part.Type == "PointLight")
            {
                var light = new OmniLight3D { Name = part.Name, Position = part.Position, LightColor = part.Color, LightEnergy = part.Brightness, OmniRange = part.Range };
                light.SetMeta("novus_id", part.Id);
                root.AddChild(light);
                continue;
            }
            if (part.Type == "SurfaceLight")
            {
                var light = new SpotLight3D { Name = part.Name, Position = part.Position, RotationDegrees = part.Rotation, LightColor = part.Color, LightEnergy = part.Brightness, SpotRange = part.Range };
                light.SetMeta("novus_id", part.Id);
                root.AddChild(light);
                continue;
            }
            if (part.Type == "Model")
            {
                var marker = new Node3D { Name = part.Name, Position = part.Position };
                marker.SetMeta("novus_id", part.Id);
                root.AddChild(marker);
                continue;
            }
            Node3D body = part.Anchored ? new StaticBody3D() : new RigidBody3D();
            body.Name = part.Name;
            body.Position = part.Position;
            body.RotationDegrees = part.Rotation;
            body.SetMeta("novus_id", part.Id);
            body.SetMeta("novus_type", "part");

            var mesh = new MeshInstance3D();
            mesh.Mesh = MeshFor(part);
            mesh.CastShadow = part.CastShadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
            mesh.MaterialOverride = ClassicPlastic.Material(part.Color, SurfaceFor(part.Material), 1f - part.Transparency, TextureScaleFor(part));
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
        if (part.Type == "Ball") return new SphereMesh { Radius = Mathf.Max(Mathf.Max(part.Size.X, part.Size.Y), part.Size.Z) / 2f, Height = part.Size.Y };
        if (part.Type == "Sphere") return new SphereMesh { Radius = Mathf.Max(Mathf.Max(part.Size.X, part.Size.Y), part.Size.Z) / 2f, Height = part.Size.Y };
        if (part.Type == "Cylinder") return new CylinderMesh { TopRadius = part.Size.X / 2f, BottomRadius = part.Size.X / 2f, Height = part.Size.Y };
        if (part.Type == "Wedge") return CreateWedge(part.Size);
        if (part.Type == "CornerWedge") return CreateCornerWedge(part.Size);
        if (part.Type == "SpawnPoint") return new CylinderMesh { TopRadius = part.Size.X / 2f, BottomRadius = part.Size.X / 2f, Height = part.Size.Y };
        return new BoxMesh { Size = part.Size };
    }

    private static Shape3D CollisionFor(NovusPart part)
    {
        if (part.Type == "Sphere" || part.Type == "Ball") return new SphereShape3D { Radius = Mathf.Max(Mathf.Max(part.Size.X, part.Size.Y), part.Size.Z) / 2f };
        if (part.Type == "Cylinder") return new CylinderShape3D { Radius = part.Size.X / 2f, Height = part.Size.Y };
        return new BoxShape3D { Size = part.Size };
    }

    private static Texture2D? SurfaceFor(string material)
    {
        LoadSurfaces();
        if (material.Equals("Neon", System.StringComparison.OrdinalIgnoreCase) || material.Equals("Glass", System.StringComparison.OrdinalIgnoreCase))
            return null;
        var index = System.Array.FindIndex(SurfaceOrder, item => item.Equals(material, System.StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index < SurfaceTextures.Length ? SurfaceTextures[index] : SurfaceTextures[0];
    }

    private static Vector2 TextureScaleFor(NovusPart part)
    {
        var tileStuds = 2f;
        return new Vector2(
            Mathf.Max(1f, part.Size.X / tileStuds),
            Mathf.Max(1f, part.Size.Z / tileStuds)
        );
    }

    private static void LoadSurfaces()
    {
        if (surfacesLoaded) return;
        surfacesLoaded = true;
        var texture = GD.Load<Texture2D>("res://assets/environment/surfaces.png");
        var image = texture?.GetImage();
        if (image == null) return;
        var tile = image.GetWidth();
        for (var i = 0; i < SurfaceTextures.Length; i++)
        {
            var region = image.GetRegion(new Rect2I(0, i * tile, tile, tile));
            SurfaceTextures[i] = ImageTexture.CreateFromImage(region);
        }
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

    private static ArrayMesh CreateCornerWedge(Vector3 size)
    {
        var hx = size.X / 2f;
        var hy = size.Y / 2f;
        var hz = size.Z / 2f;
        var verts = new[]
        {
            new Vector3(-hx, -hy, -hz),
            new Vector3(hx, -hy, -hz),
            new Vector3(-hx, -hy, hz),
            new Vector3(hx, -hy, hz),
            new Vector3(-hx, hy, -hz),
            new Vector3(hx, hy, -hz)
        };
        var faces = new[] { 0, 1, 3, 0, 3, 2, 0, 4, 5, 0, 5, 1, 1, 5, 3, 0, 2, 4, 2, 3, 5, 2, 5, 4 };
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var index in faces) st.AddVertex(verts[index]);
        st.GenerateNormals();
        return st.Commit() as ArrayMesh ?? new ArrayMesh();
    }
}
