using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

public partial class R6Character : CharacterBody3D
{
    [Export] public float WalkSpeed = 8.6f;
    [Export] public float JumpVelocity = 9.35f;
    [Export] public bool IsRemote;

    public Vector2 MobileMove { get; set; }

    private Node3D visual = null!;
    private AnimationPlayer anim = null!;
    private float gravity;
    private bool mobileJumpQueued;
    private NovusAvatar avatar = new();
    private float animClock;
    private string animState = "idle";
    private readonly List<ChatBubbleRow> bubbles = new();
    private readonly List<string> bubbleMessages = new();
    private Label3D nameLabel = null!;
    private Node3D forceField = null!;
    private MeshInstance3D? faceTexturePlane;
    private AudioStreamPlayer3D footstepSound = null!;
    private AudioStreamPlayer3D actionSound = null!;
    private int forceFieldGeneration;
    private bool localVisualHidden;
    private string activeFaceTexture = "";

    public override void _Ready()
    {
        gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
        visual = CreateVisual();
        AddChild(visual);
        anim = CreateAnimations();
        visual.AddChild(anim);
        ApplyAvatarColors();
        AddNameLabel();
        AddBubble();
        AddForceField();
        AddAudio();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsRemote) return;
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        if (MobileMove.Length() > input.Length()) input = MobileMove;
        var basis = GetViewport().GetCamera3D()?.GlobalBasis ?? Basis.Identity;
        var forward = -basis.Z; forward.Y = 0; forward = forward.Normalized();
        var right = basis.X; right.Y = 0; right = right.Normalized();
        var direction = (right * input.X + forward * -input.Y).Normalized();
        var velocity = Velocity;
        velocity.X = direction.X * WalkSpeed;
        velocity.Z = direction.Z * WalkSpeed;
        var wantsJump = Input.IsActionJustPressed("jump") || mobileJumpQueued;
        if (!IsOnFloor()) velocity.Y -= gravity * 1.85f * (float)delta;
        else if (wantsJump)
        {
            velocity.Y = JumpVelocity;
            PlayActionSound();
        }
        mobileJumpQueued = false;
        Velocity = velocity;
        MoveAndSlide();
        UpdateFootsteps(direction.Length() > 0.05f && IsOnFloor());
        if (direction.Length() > 0.05f)
        {
            LookAt(GlobalPosition + direction, Vector3.Up);
            animState = "walk";
        }
        else if (IsOnFloor()) animState = "idle";
        if (!IsOnFloor()) animState = velocity.Y > 0 ? "jump" : "fall";
        AnimateClassic((float)delta);
    }

    public override void _Process(double delta)
    {
        if (IsRemote) AnimateClassic((float)delta);
    }

    public void QueueJump()
    {
        mobileJumpQueued = true;
    }

    public void Respawn(Vector3 position)
    {
        GlobalPosition = position;
        Velocity = Vector3.Zero;
        mobileJumpQueued = false;
        animState = "idle";
        AnimateClassic(0);
        if (forceField != null) forceField.Visible = true;
        HideForceFieldLater();
    }

    public void SetAvatar(NovusAvatar nextAvatar)
    {
        avatar = nextAvatar ?? new NovusAvatar();
        if (visual != null)
        {
            ApplyAvatarColors();
            if (nameLabel != null) nameLabel.Text = avatar.Username;
        }
    }

    public void SetRemoteAnimation(string animation)
    {
        animState = string.IsNullOrWhiteSpace(animation) ? "idle" : animation;
    }

    public string CurrentAnimation => animState;

    public void SetDisplayName(string username)
    {
        avatar.Username = string.IsNullOrWhiteSpace(username) ? avatar.Username : username;
        if (nameLabel != null) nameLabel.Text = avatar.Username;
    }

    public void SetLocalVisualHidden(bool hidden)
    {
        localVisualHidden = hidden;
        if (visual != null) visual.Visible = !hidden;
        if (nameLabel != null) nameLabel.Visible = !hidden;
        foreach (var bubble in bubbles)
            bubble.Root.Visible = !hidden && !string.IsNullOrWhiteSpace(bubble.Label.Text);
        if (forceField != null && hidden) forceField.Visible = false;
    }

    public void ShowChatBubble(string message)
    {
        var clean = (message ?? "").Trim();
        if (clean.Length == 0) return;
        var bubbleText = clean.Length > 120 ? clean[..120] : clean;
        bubbleMessages.Insert(0, bubbleText);
        while (bubbleMessages.Count > 3) bubbleMessages.RemoveAt(bubbleMessages.Count - 1);
        UpdateBubbleLabels();
        var token = bubbleText;
        GetTree().CreateTimer(5).Timeout += () =>
        {
            bubbleMessages.Remove(token);
            UpdateBubbleLabels();
        };
    }

    public void HideForceFieldLater()
    {
        var generation = ++forceFieldGeneration;
        GetTree().CreateTimer(7).Timeout += () =>
        {
            if (generation == forceFieldGeneration && IsInstanceValid(forceField)) forceField.Visible = false;
        };
    }

    private Node3D CreateVisual()
    {
        return CreateLocalR6Visual() ?? CreateObjR6Visual() ?? CreatePyramidR6Visual();
    }

    private Node3D? CreateObjR6Visual()
    {
        var source = FindExternalObjR6();
        if (string.IsNullOrWhiteSpace(source)) return null;
        try
        {
            var texturePath = Path.Combine(Path.GetDirectoryName(source) ?? "", "texture.png");
            var texture = File.Exists(texturePath) ? LoadTextureFromFile(texturePath) : null;
            var groups = LoadObjGroups(source, texture);
            if (groups.Count == 0) return null;
            GD.Print($"Novus R6 loaded from OBJ: {source}");

            var sources = ClassifyObjGroups(groups);
            if (sources.HasCompleteRig)
            {
                var root = new Node3D { Name = "ObjR6Asset" };
                AddImportedPart(root, "Torso", new Vector3(0, 2.1f, 0), Vector3.Zero, new Vector3(2f, 2f, 1f), new Color(0.05f, 0.41f, 0.67f), sources.Torso!);
                AddImportedPart(root, "Head", new Vector3(0, 3.62f, 0), Vector3.Zero, new Vector3(1.38f, 1.38f, 1.38f), new Color(0.96f, 0.8f, 0.19f), sources.Head!);
                AddImportedPart(root, "LeftArm", new Vector3(-1.35f, 2.85f, 0), new Vector3(0, -0.75f, 0), new Vector3(0.7f, 1.8f, 0.8f), new Color(0.96f, 0.8f, 0.19f), sources.LeftArm!);
                AddImportedPart(root, "RightArm", new Vector3(1.35f, 2.85f, 0), new Vector3(0, -0.75f, 0), new Vector3(0.7f, 1.8f, 0.8f), new Color(0.96f, 0.8f, 0.19f), sources.RightArm!);
                AddImportedPart(root, "LeftLeg", new Vector3(-0.48f, 1.15f, 0), new Vector3(0, -0.65f, 0), new Vector3(0.85f, 1.55f, 0.85f), new Color(0.55f, 0.75f, 0.25f), sources.LeftLeg!);
                AddImportedPart(root, "RightLeg", new Vector3(0.48f, 1.15f, 0), new Vector3(0, -0.65f, 0), new Vector3(0.85f, 1.55f, 0.85f), new Color(0.55f, 0.75f, 0.25f), sources.RightLeg!);
                AddFace(root);
                return root;
            }

            var whole = new Node3D { Name = "ObjWholeR6" };
            foreach (var mesh in groups) whole.AddChild(mesh);
            NormalizeWholeImported(whole);
            ApplyClassicShaderToImported(whole, new Color(0.96f, 0.8f, 0.19f));
            AddFace(whole);
            return whole;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"OBJ R6 failed: {ex.Message}");
            return null;
        }
    }

    private Node3D? CreateLocalR6Visual()
    {
        var (imported, sourcePath) = InstantiateR6Source();
        if (imported == null) return null;
        var sources = CollectLocalR6Sources(imported);
        if (!sources.HasCompleteRig)
        {
            var wholeModel = CreateWholeImportedR6(imported, sourcePath);
            if (wholeModel != null)
            {
                GD.Print($"Novus R6 loaded as whole GLTF model: {sourcePath}");
                return wholeModel;
            }
            imported.QueueFree();
            return null;
        }
        imported.QueueFree();
        GD.Print($"Novus R6 loaded with animated part rig: {sourcePath}");

        var root = new Node3D { Name = "LocalR6Asset" };
        AddImportedPart(root, "Torso", new Vector3(0, 2.1f, 0), Vector3.Zero, new Vector3(2f, 2f, 1f), new Color(0.05f, 0.41f, 0.67f), sources.Torso);
        AddImportedPart(root, "Head", new Vector3(0, 3.62f, 0), Vector3.Zero, new Vector3(1.38f, 1.38f, 1.38f), new Color(0.96f, 0.8f, 0.19f), sources.Head);
        AddImportedPart(root, "LeftArm", new Vector3(-1.35f, 2.85f, 0), new Vector3(0, -0.75f, 0), new Vector3(0.7f, 1.8f, 0.8f), new Color(0.96f, 0.8f, 0.19f), sources.LeftArm);
        AddImportedPart(root, "RightArm", new Vector3(1.35f, 2.85f, 0), new Vector3(0, -0.75f, 0), new Vector3(0.7f, 1.8f, 0.8f), new Color(0.96f, 0.8f, 0.19f), sources.RightArm);
        AddImportedPart(root, "LeftLeg", new Vector3(-0.48f, 1.15f, 0), new Vector3(0, -0.65f, 0), new Vector3(0.85f, 1.55f, 0.85f), new Color(0.55f, 0.75f, 0.25f), sources.LeftLeg);
        AddImportedPart(root, "RightLeg", new Vector3(0.48f, 1.15f, 0), new Vector3(0, -0.65f, 0), new Vector3(0.85f, 1.55f, 0.85f), new Color(0.55f, 0.75f, 0.25f), sources.RightLeg);
        AddFace(root);
        return root;
    }

    private (Node3D? Imported, string SourcePath) InstantiateR6Source()
    {
        foreach (var path in ExternalR6Paths())
        {
            if (!File.Exists(path)) continue;
            try
            {
                var state = new GltfState();
                var document = new GltfDocument();
                var basePath = Path.GetDirectoryName(path) ?? "";
                if (document.AppendFromFile(path, state, 0, basePath) == Error.Ok && document.GenerateScene(state) is Node3D model)
                    return (model, path);
            }
            catch (Exception ex)
            {
                GD.PushWarning($"External R6 not loaded from {path}: {ex.Message}");
            }
        }

        var scene = GD.Load<PackedScene>("res://assets/r6/r6.gltf");
        return scene == null ? (null, "res://assets/r6/r6.gltf") : (scene.Instantiate<Node3D>(), "res://assets/r6/r6.gltf");
    }

    private static IEnumerable<string> ExternalR6Paths()
    {
        var exeDir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? Directory.GetCurrentDirectory();
        yield return Path.Combine(exeDir, "assets", "r6", "r6.gltf");
        yield return Path.Combine(exeDir, "r6.gltf");
        yield return Path.Combine(OS.GetUserDataDir(), "r6.gltf");
    }

    private static string FindExternalObjR6()
    {
        var exeDir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? Directory.GetCurrentDirectory();
        foreach (var path in new[]
        {
            Path.Combine(exeDir, "assets", "r6", "r6.obj"),
            Path.Combine(exeDir, "r6.obj"),
            Path.Combine(OS.GetUserDataDir(), "r6.obj"),
            @"C:\Users\Administrator\Downloads\assets (2)\r6.obj"
        })
        {
            if (File.Exists(path)) return path;
        }
        return "";
    }

    private static Texture2D? LoadTextureFromFile(string path)
    {
        var image = new Image();
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var err = ext == ".webp" ? image.LoadWebpFromBuffer(File.ReadAllBytes(path)) : image.LoadPngFromBuffer(File.ReadAllBytes(path));
        return err == Error.Ok ? ImageTexture.CreateFromImage(image) : null;
    }

    private static List<MeshInstance3D> LoadObjGroups(string path, Texture2D? texture)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var groups = new Dictionary<string, SurfaceTool>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        var current = "mesh";

        SurfaceTool ToolFor(string name)
        {
            name = string.IsNullOrWhiteSpace(name) ? "mesh" : name.Trim();
            if (!groups.TryGetValue(name, out var tool))
            {
                tool = new SurfaceTool();
                tool.Begin(Mesh.PrimitiveType.Triangles);
                groups[name] = tool;
                order.Add(name);
            }
            return tool;
        }

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            if (line.StartsWith("o ") || line.StartsWith("g "))
            {
                current = line[2..].Trim();
                continue;
            }
            if (line.StartsWith("v "))
            {
                var p = SplitObj(line);
                if (p.Length >= 4) vertices.Add(new Vector3(ParseObjFloat(p[1]), ParseObjFloat(p[2]), ParseObjFloat(p[3])));
                continue;
            }
            if (line.StartsWith("vt "))
            {
                var p = SplitObj(line);
                if (p.Length >= 3) uvs.Add(new Vector2(ParseObjFloat(p[1]), 1f - ParseObjFloat(p[2])));
                continue;
            }
            if (!line.StartsWith("f ")) continue;

            var parts = SplitObj(line);
            if (parts.Length < 4) continue;
            var face = new ObjFaceVertex[parts.Length - 1];
            for (var i = 1; i < parts.Length; i++) face[i - 1] = ParseFaceVertex(parts[i]);
            for (var i = 1; i < face.Length - 1; i++)
            {
                AddObjVertex(ToolFor(current), face[0], vertices, uvs);
                AddObjVertex(ToolFor(current), face[i], vertices, uvs);
                AddObjVertex(ToolFor(current), face[i + 1], vertices, uvs);
            }
        }

        var result = new List<MeshInstance3D>();
        foreach (var name in order)
        {
            groups[name].GenerateNormals();
            var mesh = groups[name].Commit();
            if (mesh == null || mesh.GetSurfaceCount() == 0) continue;
            var instance = new MeshInstance3D { Name = name, Mesh = mesh };
            instance.MaterialOverride = ClassicPlastic.Material(Colors.White, texture);
            result.Add(instance);
        }
        return result;
    }

    private static LocalR6Sources ClassifyObjGroups(List<MeshInstance3D> groups)
    {
        var sources = new LocalR6Sources();
        var entries = new List<(MeshInstance3D Mesh, Vector3 Center)>();
        foreach (var mesh in groups)
        {
            var aabb = mesh.Mesh?.GetAabb() ?? new Aabb();
            entries.Add((mesh, aabb.Position + aabb.Size * 0.5f));
        }
        foreach (var entry in entries)
        {
            var name = entry.Mesh.Name.ToString().ToLowerInvariant();
            if (name.Contains("head") || name.Contains("pyramid")) sources.Head = entry.Mesh;
            else if (name.Contains("torso") || name.Contains("body")) sources.Torso = entry.Mesh;
            else if (name.Contains("left") && name.Contains("arm")) sources.LeftArm = entry.Mesh;
            else if (name.Contains("right") && name.Contains("arm")) sources.RightArm = entry.Mesh;
            else if (name.Contains("left") && name.Contains("leg")) sources.LeftLeg = entry.Mesh;
            else if (name.Contains("right") && name.Contains("leg")) sources.RightLeg = entry.Mesh;
        }
        if (!sources.HasCompleteRig && entries.Count >= 6)
        {
            entries.Sort((a, b) => a.Center.Y.CompareTo(b.Center.Y));
            var legs = new List<(MeshInstance3D Mesh, Vector3 Center)> { entries[0], entries[1] };
            legs.Sort((a, b) => a.Center.X.CompareTo(b.Center.X));
            sources.LeftLeg = legs[0].Mesh;
            sources.RightLeg = legs[1].Mesh;

            var middle = new List<(MeshInstance3D Mesh, Vector3 Center)> { entries[2], entries[3], entries[4] };
            middle.Sort((a, b) => a.Center.X.CompareTo(b.Center.X));
            sources.LeftArm = middle[0].Mesh;
            sources.Torso = middle[1].Mesh;
            sources.RightArm = middle[2].Mesh;
            sources.Head = entries[^1].Mesh;
        }
        return sources;
    }

    private static void NormalizeWholeImported(Node3D root)
    {
        var bounds = CombinedAabb(root);
        if (bounds.Size.Y <= 0.001f) return;
        var scale = 4.35f / bounds.Size.Y;
        root.Scale = Vector3.One * scale;
        root.Position = new Vector3(
            -(bounds.Position.X + bounds.Size.X * 0.5f) * scale,
            -bounds.Position.Y * scale + 0.1f,
            -(bounds.Position.Z + bounds.Size.Z * 0.5f) * scale
        );
    }

    private static string[] SplitObj(string line) => line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static float ParseObjFloat(string value) => float.Parse(value, CultureInfo.InvariantCulture);

    private static ObjFaceVertex ParseFaceVertex(string token)
    {
        var pieces = token.Split('/');
        var v = int.TryParse(pieces[0], out var vertex) ? vertex - 1 : -1;
        var vt = pieces.Length > 1 && int.TryParse(pieces[1], out var uv) ? uv - 1 : -1;
        return new ObjFaceVertex(v, vt);
    }

    private static void AddObjVertex(SurfaceTool tool, ObjFaceVertex fv, List<Vector3> vertices, List<Vector2> uvs)
    {
        if (fv.Uv >= 0 && fv.Uv < uvs.Count) tool.SetUV(uvs[fv.Uv]);
        if (fv.Vertex >= 0 && fv.Vertex < vertices.Count) tool.AddVertex(vertices[fv.Vertex]);
    }

    private Node3D? CreateWholeImportedR6(Node3D imported, string sourcePath)
    {
        var meshes = new List<(MeshInstance3D Mesh, Vector3 Center)>();
        CollectMeshInstances(imported, Transform3D.Identity, meshes);
        if (meshes.Count == 0) return null;
        var bounds = CombinedAabb(imported);
        if (bounds.Size.Y <= 0.001f) return null;
        var scale = 4.35f / bounds.Size.Y;
        var root = new Node3D { Name = "LocalR6Asset" };
        imported.Name = "ImportedWholeR6";
        imported.Scale = Vector3.One * scale;
        imported.Position = new Vector3(
            -(bounds.Position.X + bounds.Size.X * 0.5f) * scale,
            -bounds.Position.Y * scale + 0.1f,
            -(bounds.Position.Z + bounds.Size.Z * 0.5f) * scale
        );
        root.AddChild(imported);
        ApplyClassicShaderToImported(root, new Color(0.96f, 0.8f, 0.19f));
        AddFace(root);
        return root;
    }

    private Node3D CreatePyramidR6Visual()
    {
        var root = new Node3D { Name = "PyramidR6" };
        AddBlock(root, "Torso", new Vector3(0, 2.1f, 0), new Vector3(2, 2, 1), new Color(0.05f, 0.41f, 0.67f));
        AddPyramidHead(root, new Vector3(0, 3.62f, 0), new Color(0.96f, 0.8f, 0.19f));
        AddPivotBlock(root, "LeftArm", new Vector3(-1.35f, 2.85f, 0), new Vector3(0, -0.75f, 0), new Vector3(0.7f, 1.8f, 0.8f), new Color(0.96f, 0.8f, 0.19f));
        AddPivotBlock(root, "RightArm", new Vector3(1.35f, 2.85f, 0), new Vector3(0, -0.75f, 0), new Vector3(0.7f, 1.8f, 0.8f), new Color(0.96f, 0.8f, 0.19f));
        AddPivotBlock(root, "LeftLeg", new Vector3(-0.48f, 1.15f, 0), new Vector3(0, -0.65f, 0), new Vector3(0.85f, 1.55f, 0.85f), new Color(0.55f, 0.75f, 0.25f));
        AddPivotBlock(root, "RightLeg", new Vector3(0.48f, 1.15f, 0), new Vector3(0, -0.65f, 0), new Vector3(0.85f, 1.55f, 0.85f), new Color(0.55f, 0.75f, 0.25f));
        AddFace(root);
        return root;
    }

    private static void AddBlock(Node root, string name, Vector3 pos, Vector3 size, Color color)
    {
        var mesh = new MeshInstance3D { Name = name, Position = pos, Mesh = new BoxMesh { Size = size } };
        mesh.MaterialOverride = ClassicPlastic.Material(color);
        root.AddChild(mesh);
    }

    private static void AddPivotBlock(Node root, string name, Vector3 pivotPos, Vector3 meshOffset, Vector3 size, Color color)
    {
        var pivot = new Node3D { Name = name, Position = pivotPos };
        var mesh = new MeshInstance3D { Name = $"{name}Mesh", Position = meshOffset, Mesh = new BoxMesh { Size = size } };
        mesh.MaterialOverride = ClassicPlastic.Material(color);
        pivot.AddChild(mesh);
        root.AddChild(pivot);
    }

    private static void AddImportedPart(Node root, string name, Vector3 pivotPos, Vector3 meshCenterOffset, Vector3 desiredSize, Color color, MeshInstance3D sourceMesh)
    {
        var meshResource = sourceMesh.Mesh;
        if (meshResource == null) return;
        var pivot = new Node3D { Name = name, Position = pivotPos };
        var mesh = new MeshInstance3D { Name = $"{name}Mesh", Mesh = meshResource };
        var aabb = meshResource.GetAabb();
        var sourceSize = aabb.Size;
        var scale = new Vector3(
            SafeScale(desiredSize.X, sourceSize.X),
            SafeScale(desiredSize.Y, sourceSize.Y),
            SafeScale(desiredSize.Z, sourceSize.Z)
        );
        var center = aabb.Position + aabb.Size * 0.5f;
        mesh.Scale = scale;
        mesh.Position = meshCenterOffset - Multiply(center, scale);
        mesh.MaterialOverride = ClassicPlastic.Material(color, ExtractAlbedoTexture(sourceMesh));
        pivot.AddChild(mesh);
        root.AddChild(pivot);
    }

    private static float SafeScale(float desired, float source)
    {
        return Mathf.Abs(source) < 0.001f ? 1f : desired / source;
    }

    private static Vector3 Multiply(Vector3 a, Vector3 b)
    {
        return new Vector3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    }

    private static LocalR6Sources CollectLocalR6Sources(Node3D imported)
    {
        var sources = new LocalR6Sources();
        var meshes = new List<(MeshInstance3D Mesh, Vector3 Center)>();
        CollectMeshInstances(imported, Transform3D.Identity, meshes);
        foreach (var entry in meshes)
        {
            var mesh = entry.Mesh;
            if (mesh.Mesh == null) continue;
            var name = mesh.Name.ToString().ToLowerInvariant();
            var pos = entry.Center;
            if (name.Contains("head") || name.Contains("pyramid") || pos.Y > 1.8f)
            {
                sources.Head = mesh;
            }
            else if ((name.Contains("torso") || name.Contains("body")) || (pos.Y > 0.75f && Mathf.Abs(pos.X) < 0.35f))
            {
                sources.Torso = mesh;
            }
            else if ((name.Contains("left") && name.Contains("arm")) || (pos.Y > 0.75f && pos.X < 0))
            {
                sources.LeftArm = mesh;
            }
            else if ((name.Contains("right") && name.Contains("arm")) || pos.Y > 0.75f)
            {
                sources.RightArm = mesh;
            }
            else if ((name.Contains("left") && name.Contains("leg")) || pos.X < 0)
            {
                sources.LeftLeg = mesh;
            }
            else
            {
                sources.RightLeg = mesh;
            }
        }
        if (!sources.HasCompleteRig && meshes.Count >= 6)
        {
            meshes.Sort((a, b) => a.Center.Y.CompareTo(b.Center.Y));
            var legs = new List<(MeshInstance3D Mesh, Vector3 Center)> { meshes[0], meshes[1] };
            legs.Sort((a, b) => a.Center.X.CompareTo(b.Center.X));
            sources.LeftLeg = legs[0].Mesh;
            sources.RightLeg = legs[1].Mesh;

            var middle = new List<(MeshInstance3D Mesh, Vector3 Center)> { meshes[2], meshes[3], meshes[4] };
            middle.Sort((a, b) => a.Center.X.CompareTo(b.Center.X));
            sources.LeftArm = middle[0].Mesh;
            sources.Torso = middle[1].Mesh;
            sources.RightArm = middle[2].Mesh;
            sources.Head = meshes[^1].Mesh;
        }
        return sources;
    }

    private static void CollectMeshInstances(Node node, Transform3D parentTransform, List<(MeshInstance3D Mesh, Vector3 Center)> meshes)
    {
        var transform = parentTransform;
        if (node is Node3D node3D) transform = parentTransform * node3D.Transform;
        if (node is MeshInstance3D mesh && mesh.Mesh != null)
        {
            var aabb = mesh.Mesh.GetAabb();
            var localCenter = aabb.Position + aabb.Size * 0.5f;
            meshes.Add((mesh, transform * localCenter));
        }
        foreach (var child in node.GetChildren())
            CollectMeshInstances(child, transform, meshes);
    }

    private static Aabb CombinedAabb(Node3D root)
    {
        var hasBounds = false;
        var bounds = new Aabb();
        CombineAabbRecursive(root, Transform3D.Identity, ref bounds, ref hasBounds);
        return hasBounds ? bounds : new Aabb(Vector3.Zero, Vector3.One);
    }

    private static void CombineAabbRecursive(Node node, Transform3D parentTransform, ref Aabb bounds, ref bool hasBounds)
    {
        var transform = parentTransform;
        if (node is Node3D node3D) transform = parentTransform * node3D.Transform;
        if (node is MeshInstance3D mesh && mesh.Mesh != null)
        {
            var local = mesh.Mesh.GetAabb();
            var corners = new[]
            {
                local.Position,
                local.Position + new Vector3(local.Size.X, 0, 0),
                local.Position + new Vector3(0, local.Size.Y, 0),
                local.Position + new Vector3(0, 0, local.Size.Z),
                local.Position + new Vector3(local.Size.X, local.Size.Y, 0),
                local.Position + new Vector3(local.Size.X, 0, local.Size.Z),
                local.Position + new Vector3(0, local.Size.Y, local.Size.Z),
                local.Position + local.Size
            };
            foreach (var corner in corners)
            {
                var point = transform * corner;
                if (!hasBounds)
                {
                    bounds = new Aabb(point, Vector3.Zero);
                    hasBounds = true;
                }
                else bounds = bounds.Expand(point);
            }
        }
        foreach (var child in node.GetChildren())
            CombineAabbRecursive(child, transform, ref bounds, ref hasBounds);
    }

    private static void ApplyClassicShaderToImported(Node node, Color fallbackColor)
    {
        if (node is MeshInstance3D mesh)
        {
            var texture = ExtractAlbedoTexture(mesh);
            var color = ExtractAlbedoColor(mesh, fallbackColor);
            mesh.MaterialOverride = ClassicPlastic.Material(color, texture);
        }
        foreach (var child in node.GetChildren())
            ApplyClassicShaderToImported(child, fallbackColor);
    }

    private static Texture2D? ExtractAlbedoTexture(MeshInstance3D mesh)
    {
        if (mesh.MaterialOverride is StandardMaterial3D overrideMat && overrideMat.AlbedoTexture is Texture2D overrideTexture) return overrideTexture;
        if (mesh.Mesh?.GetSurfaceCount() > 0 && mesh.Mesh.SurfaceGetMaterial(0) is StandardMaterial3D surfaceMat && surfaceMat.AlbedoTexture is Texture2D surfaceTexture) return surfaceTexture;
        return null;
    }

    private static Color ExtractAlbedoColor(MeshInstance3D mesh, Color fallback)
    {
        if (mesh.MaterialOverride is StandardMaterial3D overrideMat) return overrideMat.AlbedoColor;
        if (mesh.Mesh?.GetSurfaceCount() > 0 && mesh.Mesh.SurfaceGetMaterial(0) is StandardMaterial3D surfaceMat) return surfaceMat.AlbedoColor;
        return fallback;
    }

    private static void AddPyramidHead(Node root, Vector3 pivotPos, Color color)
    {
        var pivot = new Node3D { Name = "Head", Position = pivotPos };
        var mesh = new MeshInstance3D
        {
            Name = "HeadMesh",
            Mesh = CreatePyramidMesh(),
            MaterialOverride = ClassicPlastic.Material(color)
        };
        pivot.AddChild(mesh);
        root.AddChild(pivot);
    }

    private static ArrayMesh CreatePyramidMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var half = 0.68f;
        var bottom = -0.58f;
        var apex = new Vector3(0, 0.76f, 0);
        var frontLeft = new Vector3(-half, bottom, -half);
        var frontRight = new Vector3(half, bottom, -half);
        var backRight = new Vector3(half, bottom, half);
        var backLeft = new Vector3(-half, bottom, half);
        AddTriangle(st, frontLeft, frontRight, apex);
        AddTriangle(st, frontRight, backRight, apex);
        AddTriangle(st, backRight, backLeft, apex);
        AddTriangle(st, backLeft, frontLeft, apex);
        AddTriangle(st, backLeft, backRight, frontRight);
        AddTriangle(st, backLeft, frontRight, frontLeft);
        st.GenerateNormals();
        return st.Commit() as ArrayMesh ?? new ArrayMesh();
    }

    private static void AddTriangle(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c)
    {
        st.AddVertex(a);
        st.AddVertex(b);
        st.AddVertex(c);
    }

    private static void AddFace(Node3D root)
    {
        var head = root.FindChild("Head", true, false) as Node3D;
        if (head == null) return;
        var black = new StandardMaterial3D { AlbedoColor = Colors.Black, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        AddFacePiece(head, "LeftEye", new Vector3(-0.24f, 0.12f, -0.64f), new Vector3(0.08f, 0.18f, 0.025f), black);
        AddFacePiece(head, "RightEye", new Vector3(0.24f, 0.12f, -0.64f), new Vector3(0.08f, 0.18f, 0.025f), black);
        AddFacePiece(head, "Mouth", new Vector3(0, -0.22f, -0.64f), new Vector3(0.44f, 0.08f, 0.025f), black);
    }

    private static void AddFacePiece(Node3D parent, string name, Vector3 pos, Vector3 size, Material mat)
    {
        parent.AddChild(new MeshInstance3D { Name = name, Position = pos, Mesh = new BoxMesh { Size = size }, MaterialOverride = mat });
    }

    private void ApplyAvatarColors()
    {
        TintPart("Head", avatar.HeadColor);
        TintPart("Torso", avatar.TorsoColor);
        TintPart("LeftArm", avatar.ArmsColor);
        TintPart("RightArm", avatar.ArmsColor);
        TintPart("LeftLeg", avatar.LegsColor);
        TintPart("RightLeg", avatar.LegsColor);
        ApplyFace();
        RemoveExistingHats();
        foreach (var item in avatar.Items)
            if (item.Type == "hat") AddHatMarker(item);
    }

    private void AddNameLabel()
    {
        nameLabel = new Label3D
        {
            Name = "NameLabel",
            Text = avatar.Username,
            Position = new Vector3(0, 5.02f, 0),
            PixelSize = 0.014f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = Colors.White,
            OutlineModulate = Colors.Black,
            OutlineSize = 6
        };
        AddChild(nameLabel);
    }

    private void AddBubble()
    {
        for (var i = 0; i < 3; i++)
        {
            var root = new Node3D
            {
                Name = $"ChatBubble{i + 1}",
                Visible = false,
                Position = new Vector3(0, 5.45f + i * 0.48f, 0)
            };
            var backMaterial = new StandardMaterial3D
            {
                AlbedoTexture = GD.Load<Texture2D>("res://assets/ui/chat_bubble.png"),
                AlbedoColor = new Color(1f, 1f, 1f, 0.94f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                NoDepthTest = true
            };
            var back = new MeshInstance3D
            {
                Name = "BubbleBack",
                Mesh = new QuadMesh { Size = new Vector2(2.1f, 0.54f) },
                MaterialOverride = backMaterial
            };
            var label = new Label3D
            {
                Name = "BubbleText",
                Text = "",
                Position = new Vector3(0, -0.015f, -0.01f),
                PixelSize = 0.014f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Modulate = Colors.Black,
                OutlineModulate = new Color(1, 1, 1, 0.15f),
                OutlineSize = 2,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                NoDepthTest = true
            };
            root.AddChild(back);
            root.AddChild(label);
            bubbles.Add(new ChatBubbleRow(root, label, back));
            AddChild(root);
        }
    }

    private void AddForceField()
    {
        forceField = new Node3D { Name = "ForceField" };
        var purple = new Color(0.55f, 0.02f, 1f, 0.82f);
        var gold = new Color(1f, 0.92f, 0.2f, 0.62f);
        foreach (var x in new[] { -1.28f, 1.28f })
        foreach (var z in new[] { -0.68f, 0.68f })
            AddForceFieldBar(forceField, new Vector3(x, 2.15f, z), new Vector3(0.08f, 4.25f, 0.08f), purple);
        foreach (var y in new[] { 0.08f, 4.25f })
        foreach (var z in new[] { -0.68f, 0.68f })
            AddForceFieldBar(forceField, new Vector3(0, y, z), new Vector3(2.64f, 0.08f, 0.08f), purple);
        foreach (var y in new[] { 0.08f, 4.25f })
        foreach (var x in new[] { -1.28f, 1.28f })
            AddForceFieldBar(forceField, new Vector3(x, y, 0), new Vector3(0.08f, 0.08f, 1.42f), purple);
        AddForceFieldBar(forceField, new Vector3(0, 3.8f, -0.72f), new Vector3(1.45f, 0.08f, 0.08f), gold);
        AddForceFieldBar(forceField, new Vector3(0, 3.8f, 0.72f), new Vector3(1.45f, 0.08f, 0.08f), gold);
        AddChild(forceField);
        HideForceFieldLater();
    }

    private static void AddForceFieldBar(Node3D parent, Vector3 position, Vector3 size, Color color)
    {
        parent.AddChild(new MeshInstance3D
        {
            Name = "ForceFieldBar",
            Mesh = new BoxMesh { Size = size },
            Position = position,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                EmissionEnabled = true,
                Emission = color,
                EmissionEnergyMultiplier = 1.4f,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            }
        });
    }

    private void AddAudio()
    {
        var footstepStream = GD.Load<AudioStream>("res://assets/audio/bfsl-minifigfoots1.mp3");
        if (footstepStream is AudioStreamMP3 mp3) mp3.Loop = true;
        actionSound = new AudioStreamPlayer3D
        {
            Stream = GD.Load<AudioStream>("res://assets/audio/button.wav"),
            VolumeDb = -5f,
            UnitSize = 8f
        };
        AddChild(actionSound);
        footstepSound = new AudioStreamPlayer3D
        {
            Stream = footstepStream,
            VolumeDb = -7f,
            UnitSize = 9f
        };
        AddChild(footstepSound);
    }

    private void TintPart(string name, Color color)
    {
        var node = visual.FindChild(name, true, false);
        if (node is MeshInstance3D mesh)
            mesh.MaterialOverride = ClassicPlastic.Material(color, ExtractAlbedoTexture(mesh));
        else if (node is Node3D root)
        {
            foreach (var child in root.GetChildren())
                if (child is MeshInstance3D childMesh && childMesh.Name == $"{name}Mesh")
                    childMesh.MaterialOverride = ClassicPlastic.Material(color, ExtractAlbedoTexture(childMesh));
        }
    }

    private void ApplyFace()
    {
        if (!string.IsNullOrWhiteSpace(avatar.FaceTextureUrl))
        {
            SetBuiltInFaceVisible(false);
            if (avatar.FaceTextureUrl != activeFaceTexture)
            {
                activeFaceTexture = avatar.FaceTextureUrl;
                _ = ApplyFaceTextureAsync(avatar.FaceTextureUrl);
            }
            return;
        }

        SetBuiltInFaceVisible(false);
        var builtIn = $"builtin:{avatar.Face}";
        if (activeFaceTexture == builtIn) return;
        activeFaceTexture = builtIn;
        ApplyGeneratedFaceTexture(avatar.Face);
    }

    private void SetBuiltInFaceVisible(bool visible)
    {
        foreach (var name in new[] { "LeftEye", "RightEye", "Mouth" })
            if (visual.FindChild(name, true, false) is MeshInstance3D mesh)
                mesh.Visible = visible;
    }

    private async Task ApplyFaceTextureAsync(string url)
    {
        try
        {
            var bytes = await LoadImageBytes(url);
            var image = new Image();
            var lower = url.ToLowerInvariant();
            var err = lower.Contains(".jpg") || lower.Contains(".jpeg")
                ? image.LoadJpgFromBuffer(bytes)
                : lower.Contains(".webp")
                    ? image.LoadWebpFromBuffer(bytes)
                    : image.LoadPngFromBuffer(bytes);
            if (err != Error.Ok)
            {
                err = image.LoadPngFromBuffer(bytes);
                if (err != Error.Ok) err = image.LoadJpgFromBuffer(bytes);
            }
            if (err != Error.Ok)
            {
                return;
            }
            var texture = ImageTexture.CreateFromImage(image);
            var plane = EnsureFaceTexturePlane();
            plane.Visible = true;
            plane.MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = texture,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
                AlphaScissorThreshold = 0.05f,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled
            };
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Face texture not loaded: {ex.Message}");
        }
    }

    private void ApplyGeneratedFaceTexture(string face)
    {
        var image = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 0));
        DrawRect(image, 20, 19, 4, 11, Colors.Black);
        DrawRect(image, 40, 19, 4, 11, Colors.Black);
        if ((face ?? "").Contains("serious", StringComparison.OrdinalIgnoreCase))
        {
            DrawRect(image, 22, 42, 20, 4, Colors.Black);
        }
        else
        {
            DrawRect(image, 21, 41, 4, 4, Colors.Black);
            DrawRect(image, 25, 45, 4, 4, Colors.Black);
            DrawRect(image, 29, 47, 6, 3, Colors.Black);
            DrawRect(image, 35, 45, 4, 4, Colors.Black);
            DrawRect(image, 39, 41, 4, 4, Colors.Black);
        }
        var plane = EnsureFaceTexturePlane();
        plane.Visible = true;
        plane.MaterialOverride = new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(image),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
            AlphaScissorThreshold = 0.05f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
    }

    private static void DrawRect(Image image, int x, int y, int width, int height, Color color)
    {
        for (var px = x; px < x + width; px++)
        for (var py = y; py < y + height; py++)
            if (px >= 0 && py >= 0 && px < image.GetWidth() && py < image.GetHeight())
                image.SetPixel(px, py, color);
    }

    private MeshInstance3D EnsureFaceTexturePlane()
    {
        if (faceTexturePlane != null && IsInstanceValid(faceTexturePlane)) return faceTexturePlane;
        var head = visual.FindChild("Head", true, false) as Node3D;
        faceTexturePlane = new MeshInstance3D
        {
            Name = "FaceTexture",
            Position = new Vector3(0, -0.06f, -0.705f),
            Mesh = new QuadMesh { Size = new Vector2(0.82f, 0.82f) }
        };
        (head ?? visual).AddChild(faceTexturePlane);
        return faceTexturePlane;
    }

    private async Task<byte[]> LoadImageBytes(string source)
    {
        if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = source.IndexOf(',');
            if (comma < 0) return Array.Empty<byte>();
            return Convert.FromBase64String(source[(comma + 1)..]);
        }
        var http = new HttpRequest();
        AddChild(http);
        var err = http.Request(source);
        if (err != Error.Ok) throw new Exception($"HTTP request failed: {err}");
        var result = await WaitForHttp(http);
        if (result.ResponseCode >= 400) throw new Exception($"HTTP returned {result.ResponseCode}");
        return result.Body;
    }

    private void AddHatMarker(NovusAvatarItem item)
    {
        if (visual.HasNode($"NovusHat_{item.Id}")) return;
        var hatY = 4.35f + (item.HatPosition.Y - 1.2f) * 0.15f;
        var hat = new MeshInstance3D
        {
            Name = $"NovusHat_{item.Id}",
            Mesh = new CylinderMesh { TopRadius = 0.95f, BottomRadius = 1.15f, Height = 0.45f },
            Position = new Vector3(item.HatPosition.X, hatY, item.HatPosition.Z),
            RotationDegrees = item.HatRotation,
            Scale = item.HatScale
        };
        hat.MaterialOverride = ClassicPlastic.Material(new Color(0.08f, 0.08f, 0.08f));
        hat.Visible = string.IsNullOrWhiteSpace(item.ModelUrl);
        visual.AddChild(hat);
        _ = TryReplaceHatWithModel(item, hat);
    }

    private async Task TryReplaceHatWithModel(NovusAvatarItem item, MeshInstance3D fallback)
    {
        if (string.IsNullOrWhiteSpace(item.ModelUrl) || !IsInstanceValid(fallback)) return;
        try
        {
            var http = new HttpRequest();
            AddChild(http);
            var err = http.Request(item.ModelUrl);
            if (err != Error.Ok)
            {
                fallback.QueueFree();
                return;
            }
            var result = await WaitForHttp(http);
            if (result.ResponseCode >= 400 || result.Body.Length == 0)
            {
                fallback.QueueFree();
                return;
            }
            var state = new GltfState();
            var document = new GltfDocument();
            var hint = item.ModelUrl.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase) ? "novus_hat.glb" : "novus_hat.gltf";
            var modelBytes = await ModelBytesWithEmbeddedDependencies(item.ModelUrl, result.Body);
            var tempPath = Path.Combine(OS.GetUserDataDir(), $"novus_hat_{item.Id}_{Guid.NewGuid():N}_{hint}");
            File.WriteAllBytes(tempPath, modelBytes);
            var appendError = document.AppendFromFile(tempPath, state, 0, Path.GetDirectoryName(tempPath) ?? "");
            try { File.Delete(tempPath); } catch { }
            if (appendError != Error.Ok)
            {
                fallback.QueueFree();
                GD.PushWarning($"Hat GLTF append failed for {item.Name}: {appendError}");
                return;
            }
            if (document.GenerateScene(state) is not Node3D model)
            {
                fallback.QueueFree();
                return;
            }
            model.Name = fallback.Name;
            model.Position = fallback.Position;
            model.RotationDegrees = item.HatRotation;
            model.Scale = item.HatScale;
            PrepareImportedHat(model);
            visual.AddChild(model);
            fallback.QueueFree();
        }
        catch (System.Exception ex)
        {
            if (IsInstanceValid(fallback)) fallback.QueueFree();
            GD.PushWarning($"Hat model fallback for {item.Name}: {ex.Message}");
        }
    }

    private static void PrepareImportedHat(Node node)
    {
        if (node is MeshInstance3D mesh)
        {
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            mesh.MaterialOverride = ClassicPlastic.Material(ExtractAlbedoColor(mesh, new Color(0.08f, 0.08f, 0.08f)), ExtractAlbedoTexture(mesh));
        }
        foreach (var child in node.GetChildren())
            PrepareImportedHat(child);
    }

    private async Task<byte[]> ModelBytesWithEmbeddedDependencies(string modelUrl, byte[] body)
    {
        if (modelUrl.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) return body;
        try
        {
            var root = JsonNode.Parse(Encoding.UTF8.GetString(body)) as JsonObject;
            if (root == null) return body;
            await EmbedGltfUris(modelUrl, root["buffers"] as JsonArray, "application/octet-stream");
            await EmbedGltfUris(modelUrl, root["images"] as JsonArray, "image/png");
            return Encoding.UTF8.GetBytes(root.ToJsonString());
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Hat dependency embedding skipped: {ex.Message}");
            return body;
        }
    }

    private async Task EmbedGltfUris(string modelUrl, JsonArray? array, string fallbackMime)
    {
        if (array == null) return;
        foreach (var entry in array)
        {
            if (entry is not JsonObject obj) continue;
            var uri = obj["uri"]?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(uri) || uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var absolute = new Uri(new Uri(modelUrl), uri).ToString();
            var bytes = await LoadImageBytes(absolute);
            obj["uri"] = $"data:{MimeFor(uri, fallbackMime)};base64,{Convert.ToBase64String(bytes)}";
        }
    }

    private static string MimeFor(string uri, string fallback)
    {
        var lower = uri.ToLowerInvariant();
        if (lower.EndsWith(".jpg") || lower.EndsWith(".jpeg")) return "image/jpeg";
        if (lower.EndsWith(".webp")) return "image/webp";
        if (lower.EndsWith(".png")) return "image/png";
        if (lower.EndsWith(".bin")) return "application/octet-stream";
        return fallback;
    }

    private static Task<HttpResult> WaitForHttp(HttpRequest http)
    {
        var tcs = new TaskCompletionSource<HttpResult>();
        http.RequestCompleted += (result, responseCode, headers, body) =>
        {
            http.QueueFree();
            if (result != (long)HttpRequest.Result.Success) tcs.TrySetException(new System.Exception($"HTTP result {result}"));
            else tcs.TrySetResult(new HttpResult { ResponseCode = responseCode, Body = body });
        };
        return tcs.Task;
    }

    private sealed class HttpResult
    {
        public long ResponseCode;
        public byte[] Body = System.Array.Empty<byte>();
    }

    private readonly record struct ObjFaceVertex(int Vertex, int Uv);

    private sealed class LocalR6Sources
    {
        public MeshInstance3D? Head;
        public MeshInstance3D? Torso;
        public MeshInstance3D? LeftArm;
        public MeshInstance3D? RightArm;
        public MeshInstance3D? LeftLeg;
        public MeshInstance3D? RightLeg;

        public bool HasCompleteRig => Head?.Mesh != null && Torso?.Mesh != null && LeftArm?.Mesh != null && RightArm?.Mesh != null && LeftLeg?.Mesh != null && RightLeg?.Mesh != null;
    }

    private sealed class ChatBubbleRow
    {
        public ChatBubbleRow(Node3D root, Label3D label, MeshInstance3D back)
        {
            Root = root;
            Label = label;
            Back = back;
        }

        public Node3D Root { get; }
        public Label3D Label { get; }
        public MeshInstance3D Back { get; }
    }

    private AnimationPlayer CreateAnimations()
    {
        var player = new AnimationPlayer();
        var library = new AnimationLibrary();
        library.AddAnimation("idle", new Animation { Length = 1.2f, LoopMode = Animation.LoopModeEnum.Linear });
        library.AddAnimation("walk", new Animation { Length = 0.8f, LoopMode = Animation.LoopModeEnum.Linear });
        library.AddAnimation("jump", new Animation { Length = 0.4f });
        player.AddAnimationLibrary("", library);
        return player;
    }

    private void AnimateClassic(float delta)
    {
        animClock += delta;
        var rawSwing = Mathf.Sin(animClock * (animState == "walk" ? 9.25f : 2.5f));
        var steppedSwing = Mathf.Round(rawSwing * 4f) / 4f;
        var leftArmAngle = animState == "walk" ? steppedSwing * 42f : 0f;
        var rightArmAngle = animState == "walk" ? -steppedSwing * 42f : 0f;
        var legAngle = animState == "walk" ? -steppedSwing * 36f : 0f;
        if (animState == "jump") { leftArmAngle = -132f; rightArmAngle = -132f; legAngle = 10f; }
        if (animState == "fall") { leftArmAngle = -38f; rightArmAngle = -38f; legAngle = -10f; }
        RotatePart("LeftArm", new Vector3(leftArmAngle, 0, 0));
        RotatePart("RightArm", new Vector3(rightArmAngle, 0, 0));
        RotatePart("LeftLeg", new Vector3(legAngle, 0, 0));
        RotatePart("RightLeg", new Vector3(-legAngle, 0, 0));
        RotatePart("Head", Vector3.Zero);
        if (forceField != null && forceField.Visible) forceField.RotationDegrees += new Vector3(0, 90f * delta, 0);
    }

    private void UpdateBubbleLabels()
    {
        for (var i = 0; i < bubbles.Count; i++)
        {
            var hasMessage = i < bubbleMessages.Count;
            var message = hasMessage ? bubbleMessages[i] : "";
            bubbles[i].Root.Visible = hasMessage && !localVisualHidden;
            var wrapped = WrapBubbleText(message);
            bubbles[i].Label.Text = wrapped;
            var lines = string.IsNullOrWhiteSpace(wrapped) ? 1 : wrapped.Split('\n').Length;
            var longest = LongestLineLength(wrapped);
            var width = Mathf.Clamp(1.1f + longest * 0.07f, 1.8f, 4.8f);
            var height = Mathf.Clamp(0.44f + (lines - 1) * 0.28f, 0.54f, 1.18f);
            bubbles[i].Root.Position = new Vector3(0, 5.45f + i * (height + 0.14f), 0);
            if (bubbles[i].Back.Mesh is QuadMesh quad)
                quad.Size = new Vector2(width, height);
        }
    }

    private void RemoveExistingHats()
    {
        foreach (var child in visual.GetChildren())
            if (child is Node node && node.Name.ToString().StartsWith("NovusHat_", StringComparison.Ordinal))
                node.QueueFree();
    }

    private static string WrapBubbleText(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "";
        const int maxLine = 28;
        var words = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = "";
        foreach (var rawWord in words)
        {
            var word = rawWord;
            while (word.Length > maxLine)
            {
                var part = word[..maxLine];
                word = word[maxLine..];
                if (!string.IsNullOrEmpty(current))
                {
                    lines.Add(current);
                    current = "";
                }
                lines.Add(part);
            }
            if (string.IsNullOrEmpty(current)) current = word;
            else if (current.Length + 1 + word.Length <= maxLine) current += " " + word;
            else
            {
                lines.Add(current);
                current = word;
            }
            if (lines.Count >= 3) break;
        }
        if (!string.IsNullOrEmpty(current) && lines.Count < 3) lines.Add(current);
        return string.Join("\n", lines);
    }

    private static int LongestLineLength(string value)
    {
        var longest = 0;
        foreach (var line in value.Split('\n')) longest = Math.Max(longest, line.Length);
        return longest;
    }

    private void UpdateFootsteps(bool walking)
    {
        if (IsRemote || footstepSound?.Stream == null) return;
        if (walking)
        {
            if (!footstepSound.Playing) footstepSound.Play();
        }
        else if (footstepSound.Playing) footstepSound.Stop();
    }

    private void PlayActionSound()
    {
        if (actionSound?.Stream == null) return;
        actionSound.Stop();
        actionSound.Play();
    }

    private void RotatePart(string name, Vector3 degrees)
    {
        var node = visual.FindChild(name, true, false);
        if (node is Node3D part) part.RotationDegrees = degrees;
    }
}
