using Godot;
using System;
using System.Collections.Generic;

public partial class ClientMain : Node3D
{
    private NovusMap map = new();
    private R6Character player = null!;
    private Camera3D camera = null!;
    private readonly Dictionary<long, Node3D> remotePlayers = new();
    private float yaw = 35f;
    private float pitch = -18f;
    private double netClock;
    private int moveTouchId = -1;
    private int cameraTouchId = -1;
    private Vector2 moveOrigin;
    private Vector2 mobileMove;

    public override async void _Ready()
    {
        SetupInput();
        var args = OS.GetCmdlineArgs();
        var gameId = ReadArg(args, "--game", "1");
        var baseUrl = ReadArg(args, "--base-url", "http://localhost:3000");
        var serverHost = ReadArg(args, "--server", "127.0.0.1");
        var serverPort = ReadIntArg(args, "--port", 53640);
        try { map = await NovusApi.LoadPlace(baseUrl, gameId); }
        catch (Exception ex) { GD.PushWarning($"Using local baseplate: {ex.Message}"); NovusApi.EnsurePlayable(map); }
        AddChild(MapBuilder.Build(map));
        SetupLighting();
        SpawnPlayer();
        SetupMobileHud();
        ConnectMultiplayer(serverHost, serverPort);
    }

    public override void _Input(InputEvent ev)
    {
        if (ev is InputEventScreenTouch touch)
        {
            if (touch.Pressed)
            {
                if (touch.Position.X < GetViewport().GetVisibleRect().Size.X * 0.48f && moveTouchId == -1)
                {
                    moveTouchId = touch.Index;
                    moveOrigin = touch.Position;
                }
                else if (cameraTouchId == -1)
                {
                    cameraTouchId = touch.Index;
                }
            }
            else
            {
                if (touch.Index == moveTouchId)
                {
                    moveTouchId = -1;
                    mobileMove = Vector2.Zero;
                    if (player != null) player.MobileMove = Vector2.Zero;
                }
                if (touch.Index == cameraTouchId) cameraTouchId = -1;
            }
        }
        else if (ev is InputEventScreenDrag drag)
        {
            if (drag.Index == moveTouchId)
            {
                var delta = drag.Position - moveOrigin;
                mobileMove = new Vector2(delta.X, delta.Y).LimitLength(90f) / 90f;
                if (player != null) player.MobileMove = mobileMove;
            }
            else if (drag.Index == cameraTouchId)
            {
                yaw -= drag.Relative.X * 0.18f;
                pitch = Mathf.Clamp(pitch - drag.Relative.Y * 0.18f, -65, 10);
            }
        }
    }

    public override void _Process(double delta)
    {
        if (Input.IsActionPressed("camera_rotate"))
        {
            var mouse = Input.GetLastMouseVelocity();
            yaw -= mouse.X * 0.02f;
            pitch = Mathf.Clamp(pitch - mouse.Y * 0.02f, -65, 10);
        }
        if (player != null)
        {
            var rot = Mathf.DegToRad(yaw);
            var offset = new Vector3(Mathf.Sin(rot) * 12f, 7f, Mathf.Cos(rot) * 12f);
            camera.GlobalPosition = player.GlobalPosition + offset;
            camera.LookAt(player.GlobalPosition + Vector3.Up * 2f);
            netClock += delta;
            if (Multiplayer.MultiplayerPeer != null && Multiplayer.GetUniqueId() != 1 && netClock >= 0.05)
            {
                netClock = 0;
                RpcId(1, nameof(SubmitState), player.GlobalPosition, player.RotationDegrees, "move");
            }
        }
    }

    private void ConnectMultiplayer(string host, int port)
    {
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateClient(host, port);
        if (err != Error.Ok)
        {
            GD.PushWarning($"Multiplayer offline: {err}");
            return;
        }
        Multiplayer.MultiplayerPeer = peer;
        Multiplayer.ConnectedToServer += () => GD.Print("Connected to Novus Godot server");
        Multiplayer.ConnectionFailed += () => GD.PushWarning("Could not connect to Novus Godot server");
        Multiplayer.ServerDisconnected += () => GD.PushWarning("Disconnected from Novus Godot server");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    public void SubmitState(Vector3 position, Vector3 rotation, string animation) {}

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    public void ReceiveState(long id, Vector3 position, Vector3 rotation, string animation)
    {
        if (id == Multiplayer.GetUniqueId()) return;
        if (!remotePlayers.TryGetValue(id, out var remote))
        {
            remote = CreateRemotePlayer(id);
            remotePlayers[id] = remote;
            AddChild(remote);
        }
        remote.GlobalPosition = remote.GlobalPosition.Lerp(position, 0.35f);
        remote.RotationDegrees = rotation;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveChat(long id, string message) {}

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void PlayerLeft(long id)
    {
        if (!remotePlayers.TryGetValue(id, out var remote)) return;
        remote.QueueFree();
        remotePlayers.Remove(id);
    }

    private static Node3D CreateRemotePlayer(long id)
    {
        var root = new Node3D { Name = $"Player_{id}" };
        var body = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(2, 3, 1) }, Position = new Vector3(0, 1.5f, 0) };
        body.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.1f, 0.1f), Roughness = 0.7f };
        root.AddChild(body);
        return root;
    }

    private void SetupLighting()
    {
        var env = new WorldEnvironment();
        env.Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = map.SkyColor, AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.55f };
        AddChild(env);
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-45, -35, 0), LightEnergy = 1.8f, ShadowEnabled = true });
    }

    private void SpawnPlayer()
    {
        player = new R6Character { Name = "LocalPlayer", Position = map.Spawn + Vector3.Up * 2f };
        var collision = new CollisionShape3D { Shape = new CapsuleShape3D { Height = 4f, Radius = 0.9f }, Position = new Vector3(0, 2f, 0) };
        player.AddChild(collision);
        AddChild(player);
        camera = new Camera3D { Current = true, Fov = 70f };
        AddChild(camera);
    }

    private void SetupMobileHud()
    {
        var layer = new CanvasLayer { Name = "MobileHud" };
        AddChild(layer);

        var root = new Control { Name = "MobileControls" };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(root);

        var stick = new Panel
        {
            Name = "JoystickGuide",
            CustomMinimumSize = new Vector2(150, 150),
            Position = new Vector2(34, 520),
            Modulate = new Color(1, 1, 1, 0.28f)
        };
        root.AddChild(stick);

        var label = new Label
        {
            Text = "MOVE",
            Position = new Vector2(82, 584),
            Modulate = new Color(1, 1, 1, 0.72f)
        };
        root.AddChild(label);

        var jump = new Button
        {
            Text = "PULAR",
            Position = new Vector2(1110, 560),
            Size = new Vector2(130, 72)
        };
        jump.Pressed += () => player?.QueueJump();
        root.AddChild(jump);
    }

    private static void SetupInput()
    {
        Bind("move_forward", Key.W);
        Bind("move_back", Key.S);
        Bind("move_left", Key.A);
        Bind("move_right", Key.D);
        Bind("jump", Key.Space);
        BindMouse("camera_rotate", MouseButton.Right);
    }

    private static void Bind(string action, Key key)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        var ev = new InputEventKey { Keycode = key };
        if (!InputMap.ActionHasEvent(action, ev)) InputMap.ActionAddEvent(action, ev);
    }

    private static void BindMouse(string action, MouseButton button)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
    }

    private static string ReadArg(string[] args, string name, string fallback)
    {
        for (var i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return fallback;
    }

    private static int ReadIntArg(string[] args, string name, int fallback)
    {
        for (var i = 0; i < args.Length - 1; i++) if (args[i] == name && int.TryParse(args[i + 1], out var value)) return value;
        return fallback;
    }
}
