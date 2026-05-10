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
    private NovusAvatar localAvatar = new();
    private Label playerList = null!;
    private Label healthLabel = null!;
    private ColorRect healthFill = null!;
    private RichTextLabel chatLog = null!;
    private LineEdit chatInput = null!;
    private readonly HashSet<long> knownPlayers = new();
    private readonly Dictionary<long, string> playerNames = new();
    private double uiClock;

    public override async void _Ready()
    {
        SetupInput();
        var args = OS.GetCmdlineArgs();
        var gameId = ReadArg(args, "--game", "1");
        var baseUrl = ReadArg(args, "--base-url", "http://localhost:3000");
        var ticket = ReadArg(args, "--ticket", "");
        var serverHost = ReadArg(args, "--server", "127.0.0.1");
        var serverPort = ReadIntArg(args, "--port", 53640);
        try { map = await NovusApi.LoadPlace(baseUrl, gameId); }
        catch (Exception ex) { GD.PushWarning($"Using local baseplate: {ex.Message}"); NovusApi.EnsurePlayable(map); }
        AddChild(MapBuilder.Build(map));
        SetupLighting();
        SpawnPlayer();
        try { localAvatar = await NovusApi.LoadAvatar(baseUrl, ticket); player.SetAvatar(localAvatar); }
        catch (Exception ex) { GD.PushWarning($"Avatar not loaded: {ex.Message}"); }
        SetupDesktopHud();
        if (IsMobileDevice()) SetupMobileHud();
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
                RpcId(1, nameof(SubmitState), player.GlobalPosition, player.RotationDegrees, player.CurrentAnimation);
            }
            uiClock += delta;
            if (uiClock > 0.5)
            {
                uiClock = 0;
                UpdatePlayerList();
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
        Multiplayer.ConnectedToServer += () =>
        {
            GD.Print("Connected to Novus Godot server");
            RpcId(1, nameof(RegisterPlayer), localAvatar.Username);
        };
        Multiplayer.ConnectionFailed += () => GD.PushWarning("Could not connect to Novus Godot server");
        Multiplayer.ServerDisconnected += () => GD.PushWarning("Disconnected from Novus Godot server");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    public void SubmitState(Vector3 position, Vector3 rotation, string animation) {}

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void RegisterPlayer(string username) {}

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
        if (remote is R6Character r6) r6.SetRemoteAnimation(animation);
        knownPlayers.Add(id);
        if (!playerNames.ContainsKey(id)) playerNames[id] = $"Player{id}";
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveChat(long id, string message)
    {
        var clean = Moderate(message);
        AddChatLine($"{PlayerName(id)}: {clean}");
        if (id == Multiplayer.GetUniqueId()) player.ShowChatBubble(clean);
        else if (remotePlayers.TryGetValue(id, out var remote) && remote is R6Character r6) r6.ShowChatBubble(clean);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void PlayerJoined(long id, string username)
    {
        knownPlayers.Add(id);
        playerNames[id] = username;
        AddChatLine($"{username} entrou no jogo.");
        UpdatePlayerList();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void PlayerLeft(long id)
    {
        if (!remotePlayers.TryGetValue(id, out var remote)) return;
        remote.QueueFree();
        remotePlayers.Remove(id);
        knownPlayers.Remove(id);
        playerNames.Remove(id);
        UpdatePlayerList();
    }

    private static Node3D CreateRemotePlayer(long id)
    {
        return new R6Character { Name = $"Player_{id}", IsRemote = true };
    }

    private void SetupLighting()
    {
        var env = new WorldEnvironment();
        env.Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(1f, 0.38f, 0.08f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(1f, 0.72f, 0.46f),
            AmbientLightEnergy = 0.65f,
            FogEnabled = true,
            FogLightColor = new Color(1f, 0.55f, 0.18f),
            FogDensity = 0.0025f
        };
        AddChild(env);
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-35, -25, 0), LightEnergy = 2.2f, ShadowEnabled = true });
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

    private void SetupDesktopHud()
    {
        var layer = new CanvasLayer { Name = "ClassicHud" };
        AddChild(layer);
        var root = new Control();
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(root);

        var top = Panel(new Vector2(0, 0), new Vector2(1280, 26), new Color(0.82f, 0.86f, 0.88f, 0.72f));
        root.AddChild(top);
        top.AddChild(new Label { Text = "Novus Worlds", Position = new Vector2(12, 4), Modulate = Colors.White });
        foreach (var item in new[] { "Reset", "Help", "Exit" })
            top.AddChild(new Label { Text = item, Position = new Vector2(170 + top.GetChildCount() * 80, 4), Modulate = new Color(0.12f, 0.12f, 0.12f) });

        var healthPanel = new Control { Position = new Vector2(1130, 290), Size = new Vector2(90, 170) };
        root.AddChild(healthPanel);
        healthFill = new ColorRect { Position = new Vector2(35, 0), Size = new Vector2(7, 138), Color = new Color(0.38f, 0.78f, 0.22f) };
        healthPanel.AddChild(healthFill);
        healthLabel = new Label { Text = "Health", Position = new Vector2(14, 140), Modulate = Colors.Blue };
        healthPanel.AddChild(healthLabel);

        var listPanel = Panel(new Vector2(1048, 10), new Vector2(210, 180), new Color(0.75f, 0.78f, 0.82f, 0.58f));
        root.AddChild(listPanel);
        playerList = new Label { Text = "Player List", Position = new Vector2(8, 5), Modulate = Colors.White };
        listPanel.AddChild(playerList);

        var inventory = Panel(new Vector2(8, 625), new Vector2(300, 86), new Color(0.72f, 0.74f, 0.76f, 0.48f));
        root.AddChild(inventory);
        for (var i = 0; i < 5; i++)
        {
            var slot = Panel(new Vector2(8 + i * 56, 18), new Vector2(48, 48), new Color(0.9f, 0.94f, 0.98f, 0.72f));
            slot.AddChild(new Label { Text = (i + 1).ToString(), Position = new Vector2(3, 28), Modulate = Colors.White });
            inventory.AddChild(slot);
        }

        chatLog = new RichTextLabel { Position = new Vector2(8, 28), Size = new Vector2(330, 118), BbcodeEnabled = false, ScrollActive = true, Modulate = Colors.White };
        root.AddChild(chatLog);
        chatInput = new LineEdit { PlaceholderText = "To chat click here or press the / key", Position = new Vector2(8, 690), Size = new Vector2(520, 24) };
        chatInput.TextSubmitted += SendChatMessage;
        root.AddChild(chatInput);
        AddChatLine("Bem-vindo ao Novus Worlds.");
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

    private static Panel Panel(Vector2 position, Vector2 size, Color color)
    {
        var panel = new Panel { Position = position, Size = size };
        var style = new StyleBoxFlat { BgColor = color, BorderColor = new Color(0.45f, 0.45f, 0.45f), BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1 };
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    private void SendChatMessage(string raw)
    {
        var msg = Moderate(raw).Trim();
        chatInput.Text = "";
        if (msg.Length == 0) return;
        RpcId(1, nameof(SendChat), msg);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void SendChat(string message) {}

    private void AddChatLine(string line)
    {
        chatLog?.AppendText(line + "\n");
    }

    private void UpdatePlayerList()
    {
        if (playerList == null) return;
        var text = "Player List\n" + localAvatar.Username + "\n";
        foreach (var id in knownPlayers)
            if (id != Multiplayer.GetUniqueId()) text += PlayerName(id) + "\n";
        playerList.Text = text;
    }

    private string PlayerName(long id) => playerNames.TryGetValue(id, out var name) ? name : $"Player{id}";

    private static string Moderate(string message)
    {
        var text = (message ?? "").Trim();
        foreach (var bad in new[] { "porra", "caralho", "merda" })
            text = text.Replace(bad, "****", StringComparison.OrdinalIgnoreCase);
        return text.Length > 120 ? text[..120] : text;
    }

    private static bool IsMobileDevice()
    {
        var os = OS.GetName();
        return os == "Android" || os == "iOS";
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
