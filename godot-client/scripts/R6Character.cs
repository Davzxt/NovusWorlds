using Godot;

public partial class R6Character : CharacterBody3D
{
    [Export] public float WalkSpeed = 13f;
    [Export] public float JumpVelocity = 7.5f;

    public Vector2 MobileMove { get; set; }

    private Node3D visual = null!;
    private AnimationPlayer anim = null!;
    private float gravity;
    private bool mobileJumpQueued;

    public override void _Ready()
    {
        gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
        visual = CreateVisual();
        AddChild(visual);
        anim = CreateAnimations();
        visual.AddChild(anim);
    }

    public override void _PhysicsProcess(double delta)
    {
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        if (MobileMove.Length() > input.Length()) input = MobileMove;
        var basis = GetViewport().GetCamera3D()?.GlobalBasis ?? Basis.Identity;
        var forward = -basis.Z; forward.Y = 0; forward = forward.Normalized();
        var right = basis.X; right.Y = 0; right = right.Normalized();
        var direction = (right * input.X + forward * -input.Y).Normalized();
        var velocity = Velocity;
        velocity.X = direction.X * WalkSpeed;
        velocity.Z = direction.Z * WalkSpeed;
        if (!IsOnFloor()) velocity.Y -= gravity * (float)delta;
        else if (Input.IsActionJustPressed("jump") || mobileJumpQueued) velocity.Y = JumpVelocity;
        mobileJumpQueued = false;
        Velocity = velocity;
        MoveAndSlide();
        if (direction.Length() > 0.05f)
        {
            LookAt(GlobalPosition + direction, Vector3.Up);
            if (anim.CurrentAnimation != "walk") anim.Play("walk");
        }
        else if (IsOnFloor() && anim.CurrentAnimation != "idle") anim.Play("idle");
        if (!IsOnFloor() && anim.CurrentAnimation != "jump") anim.Play("jump");
    }

    public void QueueJump()
    {
        mobileJumpQueued = true;
    }

    private Node3D CreateVisual()
    {
        var model = GD.Load<PackedScene>("res://assets/r6/r6.gltf");
        if (model != null) return model.Instantiate<Node3D>();
        var root = new Node3D { Name = "R6Fallback" };
        AddBlock(root, "Torso", new Vector3(0, 1.8f, 0), new Vector3(2, 2, 1), new Color(0.05f, 0.41f, 0.67f));
        AddBlock(root, "Head", new Vector3(0, 3.2f, 0), new Vector3(1.2f, 1.2f, 1.2f), new Color(0.96f, 0.8f, 0.19f));
        AddBlock(root, "LeftArm", new Vector3(-1.55f, 1.8f, 0), new Vector3(0.7f, 2, 0.8f), new Color(0.96f, 0.8f, 0.19f));
        AddBlock(root, "RightArm", new Vector3(1.55f, 1.8f, 0), new Vector3(0.7f, 2, 0.8f), new Color(0.96f, 0.8f, 0.19f));
        AddBlock(root, "LeftLeg", new Vector3(-0.5f, 0.55f, 0), new Vector3(0.8f, 1.6f, 0.8f), new Color(0.1f, 0.16f, 0.21f));
        AddBlock(root, "RightLeg", new Vector3(0.5f, 0.55f, 0), new Vector3(0.8f, 1.6f, 0.8f), new Color(0.1f, 0.16f, 0.21f));
        return root;
    }

    private static void AddBlock(Node root, string name, Vector3 pos, Vector3 size, Color color)
    {
        var mesh = new MeshInstance3D { Name = name, Position = pos, Mesh = new BoxMesh { Size = size } };
        mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.75f };
        root.AddChild(mesh);
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
}
