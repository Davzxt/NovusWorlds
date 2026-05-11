using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

public partial class R6Character : CharacterBody3D
{
    [Export] public float WalkSpeed = 8.5f;
    [Export] public float JumpVelocity = 8.4f;
    [Export] public bool IsRemote;

    public Vector2 MobileMove { get; set; }

    private Node3D visual = null!;
    private AnimationPlayer anim = null!;
    private float gravity;
    private bool mobileJumpQueued;
    private NovusAvatar avatar = new();
    private float animClock;
    private string animState = "idle";
    private Label3D bubble = null!;
    private Label3D nameLabel = null!;
    private MeshInstance3D forceField = null!;

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
        if (!IsOnFloor()) velocity.Y -= gravity * 1.85f * (float)delta;
        else if (Input.IsActionJustPressed("jump") || mobileJumpQueued) velocity.Y = JumpVelocity;
        mobileJumpQueued = false;
        Velocity = velocity;
        MoveAndSlide();
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

    public void ShowChatBubble(string message)
    {
        if (bubble == null) return;
        bubble.Text = message;
        bubble.Visible = true;
        GetTree().CreateTimer(5).Timeout += () => { if (IsInstanceValid(bubble)) bubble.Visible = false; };
    }

    public void HideForceFieldLater()
    {
        GetTree().CreateTimer(7).Timeout += () => { if (IsInstanceValid(forceField)) forceField.Visible = false; };
    }

    private Node3D CreateVisual()
    {
        var imported = CreateImportedR6Visual();
        return imported ?? CreateBlockR6Visual();
    }

    private Node3D CreateImportedR6Visual()
    {
        var scene = GD.Load<PackedScene>("res://assets/r6/r6.gltf");
        if (scene == null) return null;
        var source = scene.Instantiate<Node3D>();
        var meshes = new List<MeshInstance3D>();
        CollectMeshes(source, meshes);
        if (meshes.Count < 6) return null;

        meshes.Sort((a, b) => a.GlobalPosition.Y.CompareTo(b.GlobalPosition.Y));
        var root = new Node3D { Name = "ClassicR6" };
        const float modelScale = 1.58f;

        var legA = meshes[0];
        var legB = meshes[1];
        var head = meshes[^1];
        var middle = new List<MeshInstance3D> { meshes[2], meshes[3], meshes[4] };
        middle.Sort((a, b) => Mathf.Abs(a.GlobalPosition.X).CompareTo(Mathf.Abs(b.GlobalPosition.X)));
        var torso = middle[0];
        var armA = middle[1];
        var armB = middle[2];

        AdoptPart(root, "Torso", torso, modelScale, false);
        AdoptPart(root, armA.GlobalPosition.X < armB.GlobalPosition.X ? "LeftArm" : "RightArm", armA, modelScale, true);
        AdoptPart(root, armA.GlobalPosition.X < armB.GlobalPosition.X ? "RightArm" : "LeftArm", armB, modelScale, true);
        AdoptPart(root, legA.GlobalPosition.X < legB.GlobalPosition.X ? "LeftLeg" : "RightLeg", legA, modelScale, true);
        AdoptPart(root, legA.GlobalPosition.X < legB.GlobalPosition.X ? "RightLeg" : "LeftLeg", legB, modelScale, true);
        AdoptPart(root, "Head", head, modelScale, false);
        AddFace(root);
        source.QueueFree();
        return root;
    }

    private static void CollectMeshes(Node node, List<MeshInstance3D> meshes)
    {
        if (node is MeshInstance3D mesh && mesh.Mesh != null) meshes.Add(mesh);
        foreach (var child in node.GetChildren()) CollectMeshes(child, meshes);
    }

    private static void AdoptPart(Node3D root, string name, MeshInstance3D mesh, float scale, bool pivotAtTop)
    {
        var sourcePosition = mesh.GlobalPosition;
        var aabb = mesh.GetAabb();
        var localTop = aabb.Position.Y + aabb.Size.Y;
        mesh.GetParent()?.RemoveChild(mesh);
        var pivot = new Node3D { Name = name };
        if (pivotAtTop)
        {
            pivot.Position = new Vector3(sourcePosition.X * scale, (sourcePosition.Y + localTop) * scale, sourcePosition.Z * scale);
            mesh.Position = new Vector3(0, -localTop * scale, 0);
        }
        else
        {
            pivot.Position = sourcePosition * scale;
            mesh.Position = Vector3.Zero;
        }
        mesh.Name = $"{name}Mesh";
        mesh.Scale *= scale;
        pivot.AddChild(mesh);
        root.AddChild(pivot);
    }

    private Node3D CreateBlockR6Visual()
    {
        var root = new Node3D { Name = "ClassicR6" };
        AddBlock(root, "Torso", new Vector3(0, 2.1f, 0), new Vector3(2, 2, 1), new Color(0.05f, 0.41f, 0.67f));
        AddPivotBlock(root, "Head", new Vector3(0, 3.65f, 0), Vector3.Zero, new Vector3(1.25f, 1.25f, 1.25f), new Color(0.96f, 0.8f, 0.19f));
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
        mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.75f };
        root.AddChild(mesh);
    }

    private static void AddPivotBlock(Node root, string name, Vector3 pivotPos, Vector3 meshOffset, Vector3 size, Color color)
    {
        var pivot = new Node3D { Name = name, Position = pivotPos };
        var mesh = new MeshInstance3D { Name = $"{name}Mesh", Position = meshOffset, Mesh = new BoxMesh { Size = size } };
        mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.68f };
        pivot.AddChild(mesh);
        root.AddChild(pivot);
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
        bubble = new Label3D
        {
            Name = "ChatBubble",
            Text = "",
            Visible = false,
            Position = new Vector3(0, 5.45f, 0),
            PixelSize = 0.018f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = Colors.White,
            OutlineModulate = Colors.Black,
            OutlineSize = 8
        };
        AddChild(bubble);
    }

    private void AddForceField()
    {
        forceField = new MeshInstance3D
        {
            Name = "ForceField",
            Mesh = new BoxMesh { Size = new Vector3(3.2f, 4.7f, 1.8f) },
            Position = new Vector3(0, 2.1f, 0),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.55f, 0.05f, 1f, 0.38f),
                EmissionEnabled = true,
                Emission = new Color(0.55f, 0.05f, 1f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            }
        };
        AddChild(forceField);
        HideForceFieldLater();
    }

    private void TintPart(string name, Color color)
    {
        var node = visual.FindChild(name, true, false);
        if (node is MeshInstance3D mesh)
            mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.72f };
        else if (node is Node3D root)
        {
            foreach (var child in root.GetChildren())
                if (child is MeshInstance3D childMesh && childMesh.Name == $"{name}Mesh")
                    childMesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.72f };
        }
    }

    private void ApplyFace()
    {
        var mouth = visual.FindChild("Mouth", true, false) as MeshInstance3D;
        if (mouth == null) return;
        mouth.Scale = avatar.Face.Contains("serious") ? new Vector3(0.8f, 0.7f, 1) : Vector3.One;
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
        hat.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.08f, 0.08f), Roughness = 0.55f };
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
            if (err != Error.Ok) return;
            var result = await WaitForHttp(http);
            if (result.ResponseCode >= 400 || result.Body.Length == 0) return;
            var state = new GltfState();
            var document = new GltfDocument();
            if (document.AppendFromBuffer(result.Body, "", state) != Error.Ok) return;
            if (document.GenerateScene(state) is not Node3D model) return;
            model.Name = fallback.Name;
            model.Position = fallback.Position;
            model.RotationDegrees = item.HatRotation;
            model.Scale = item.HatScale;
            visual.AddChild(model);
            fallback.QueueFree();
        }
        catch (System.Exception ex)
        {
            GD.PushWarning($"Hat model fallback for {item.Name}: {ex.Message}");
        }
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
        var swing = Mathf.Sin(animClock * (animState == "walk" ? 8.5f : 2.5f));
        var armAngle = animState == "walk" ? swing * 24f : 0f;
        var legAngle = animState == "walk" ? -swing * 24f : 0f;
        if (animState == "jump") { armAngle = -28f; legAngle = 12f; }
        if (animState == "fall") { armAngle = 18f; legAngle = -8f; }
        RotatePart("LeftArm", new Vector3(armAngle, 0, 0));
        RotatePart("RightArm", new Vector3(-armAngle, 0, 0));
        RotatePart("LeftLeg", new Vector3(legAngle, 0, 0));
        RotatePart("RightLeg", new Vector3(-legAngle, 0, 0));
        RotatePart("Head", Vector3.Zero);
        if (forceField != null && forceField.Visible) forceField.RotationDegrees += new Vector3(0, 90f * delta, 0);
    }

    private void RotatePart(string name, Vector3 degrees)
    {
        var node = visual.FindChild(name, true, false);
        if (node is Node3D part) part.RotationDegrees = degrees;
    }
}
