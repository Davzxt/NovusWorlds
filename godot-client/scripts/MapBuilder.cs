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

            var mesh = new MeshInstance3D();
            mesh.Mesh = part.Type == "Sphere" ? new SphereMesh { Radius = Mathf.Max(Mathf.Max(part.Size.X, part.Size.Y), part.Size.Z) / 2f } : new BoxMesh { Size = part.Size };
            var material = new StandardMaterial3D
            {
                AlbedoColor = new Color(part.Color.R, part.Color.G, part.Color.B, 1f - part.Transparency),
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
        }
        return root;
    }
}
