using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

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
    private float cameraDistance = 12f;
    private bool rotatingCamera;
    private AudioStreamPlayer switchSound = null!;
    private Control topBar = null!;
    private Control playerListPanel = null!;
    private Control healthPanel = null!;
    private Control inventoryPanel = null!;
    private Control chatPanel = null!;

    public override async void _Ready()
    {
        SetupInput();
        SetupAssets();
        var args = OS.GetCmdlineArgs();
        var launch = ReadLaunchData(args);
        try { map = await NovusApi.LoadPlace(launch.BaseUrl, launch.GameId, launch.Ticket); }
        catch (Exception ex) { GD.PushWarning($"Using local baseplate: {ex.Message}"); NovusApi.EnsurePlayable(map); }
        AddChild(MapBuilder.Build(map));
        SetupLighting();
        SpawnPlayer();
        try { localAvatar = await NovusApi.LoadAvatar(launch.BaseUrl, launch.Ticket); player.SetAvatar(localAvatar); player.SetDisplayName(localAvatar.Username); }
        catch (Exception ex) { GD.PushWarning($"Avatar not loaded: {ex.Message}"); }
        SetupDesktopHud();
        if (IsMobileDevice()) SetupMobileHud();
        ConnectMultiplayer(launch.ServerHost, launch.ServerPort);
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
        else if (ev is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Right) rotatingCamera = mouseButton.Pressed;
            if (mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.WheelUp)
            {
                cameraDistance = Mathf.Max(6f, cameraDistance - 1.2f);
                PlaySwitch();
            }
            if (mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.WheelDown)
            {
                cameraDistance = Mathf.Min(28f, cameraDistance + 1.2f);
                PlaySwitch();
            }
        }
        else if (ev is InputEventMouseMotion motion && rotatingCamera)
        {
            yaw -= motion.Relative.X * 0.18f;
            pitch = Mathf.Clamp(pitch - motion.Relative.Y * 0.18f, 8f, 68f);
        }
        else if (ev is InputEventKey key && key.Pressed)
        {
            if (key.Keycode == Key.Slash)
            {
                chatInput?.GrabFocus();
                PlaySwitch();
            }
            if (key.Keycode == Key.Escape) chatInput?.ReleaseFocus();
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
                pitch = Mathf.Clamp(pitch - drag.Relative.Y * 0.18f, 8f, 68f);
            }
        }
    }

    public override void _Process(double delta)
    {
        if (player != null)
        {
            var yawRad = Mathf.DegToRad(yaw);
            var pitchRad = Mathf.DegToRad(pitch);
            var horizontal = Mathf.Cos(pitchRad) * cameraDistance;
            var offset = new Vector3(Mathf.Sin(yawRad) * horizontal, Mathf.Sin(pitchRad) * cameraDistance + 1.2f, Mathf.Cos(yawRad) * horizontal);
            camera.GlobalPosition = camera.GlobalPosition.Lerp(player.GlobalPosition + offset, 0.35f);
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
                LayoutHud();
            }
        }
    }

    private void SetupAssets()
    {
        var cursor = GD.Load<Texture2D>("res://assets/ui/ArrowCursor.png");
        if (cursor != null) Input.SetCustomMouseCursor(cursor, Input.CursorShape.Arrow, Vector2.Zero);
        switchSound = new AudioStreamPlayer { Stream = GD.Load<AudioStream>("res://assets/ui/SWITCH3.wav"), VolumeDb = -8f };
        AddChild(switchSound);
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
        if (id == Multiplayer.GetUniqueId()) return;
        var clean = Moderate(message);
        AddChatLine($"{PlayerName(id)}: {clean}");
        if (remotePlayers.TryGetValue(id, out var remote) && remote is R6Character r6) r6.ShowChatBubble(clean);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveChatHistory(string history)
    {
        foreach (var line in (history ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            AddChatLine(line);
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
        var sky = new Sky();
        var panorama = GD.Load<Texture2D>("res://assets/environment/skybox.webp");
        if (panorama != null)
        {
            sky.SkyMaterial = new PanoramaSkyMaterial
            {
                Panorama = panorama,
                EnergyMultiplier = 1.0f
            };
        }
        else
        {
            sky.SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(0.34f, 0.62f, 1f),
                SkyHorizonColor = new Color(0.92f, 0.96f, 1f),
                GroundBottomColor = new Color(0.72f, 0.78f, 0.86f),
                GroundHorizonColor = new Color(0.98f, 0.98f, 1f),
                SunAngleMax = 18f,
                SunCurve = 0.08f
            };
        }
        env.Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.78f, 0.86f, 1f),
            AmbientLightEnergy = 0.8f,
            FogEnabled = true,
            FogLightColor = new Color(0.78f, 0.88f, 1f),
            FogDensity = 0.0015f
        };
        AddChild(env);
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, -35, 0), LightEnergy = 1.65f, ShadowEnabled = true });
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

        topBar = Panel(Vector2.Zero, new Vector2(1280, 25), new Color(0.80f, 0.82f, 0.84f, 0.76f));
        root.AddChild(topBar);
        topBar.AddChild(new Label { Text = "Novus Worlds", Position = new Vector2(10, 4), Modulate = Colors.White });
        AddTopButton("Reset", 250, () => { player.GlobalPosition = map.Spawn + Vector3.Up * 2f; PlaySwitch(); });
        AddTopButton("Help", 330, () => { AddChatLine("WASD move, right mouse rotates camera, mouse wheel zooms, / opens chat."); PlaySwitch(); });
        AddTopButton("Exit", 410, () => { PlaySwitch(); GetTree().Quit(); });

        healthPanel = new Control { Position = new Vector2(1130, 290), Size = new Vector2(90, 170) };
        root.AddChild(healthPanel);
        healthFill = new ColorRect { Position = new Vector2(35, 0), Size = new Vector2(7, 138), Color = new Color(0.38f, 0.78f, 0.22f) };
        healthPanel.AddChild(healthFill);
        healthLabel = new Label { Text = "Health", Position = new Vector2(14, 140), Modulate = Colors.Blue };
        healthPanel.AddChild(healthLabel);

        playerListPanel = Panel(new Vector2(1048, 32), new Vector2(210, 180), new Color(0.72f, 0.72f, 0.72f, 0.52f));
        root.AddChild(playerListPanel);
        playerList = new Label { Text = "Player List", Position = new Vector2(8, 5), Modulate = Colors.White };
        playerListPanel.AddChild(playerList);

        inventoryPanel = Panel(new Vector2(8, 625), new Vector2(300, 76), new Color(0.72f, 0.74f, 0.76f, 0.36f));
        root.AddChild(inventoryPanel);
        for (var i = 0; i < 5; i++)
        {
            var slot = Panel(new Vector2(8 + i * 56, 18), new Vector2(48, 48), new Color(0.9f, 0.94f, 0.98f, 0.72f));
            slot.AddChild(new Label { Text = (i + 1).ToString(), Position = new Vector2(3, 28), Modulate = Colors.White });
            inventoryPanel.AddChild(slot);
        }

        chatPanel = new Control { Position = new Vector2(8, 32), Size = new Vector2(360, 136) };
        root.AddChild(chatPanel);
        chatLog = new RichTextLabel { Position = Vector2.Zero, Size = new Vector2(360, 112), BbcodeEnabled = false, ScrollActive = true, Modulate = Colors.White };
        chatPanel.AddChild(chatLog);
        chatInput = new LineEdit { PlaceholderText = "To chat click here or press the / key", Position = new Vector2(8, 690), Size = new Vector2(520, 24) };
        chatInput.TextSubmitted += SendChatMessage;
        root.AddChild(chatInput);
        AddChatLine("Bem-vindo ao Novus Worlds.");
        LayoutHud();
    }

    private void AddTopButton(string text, float x, Action action)
    {
        var button = new Button { Text = text, Position = new Vector2(x, 0), Size = new Vector2(72, 24), Flat = true };
        button.Pressed += action;
        topBar.AddChild(button);
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
        AddChatLine($"{localAvatar.Username}: {msg}");
        player.ShowChatBubble(msg);
        if (Multiplayer.MultiplayerPeer != null && Multiplayer.GetUniqueId() != 1) RpcId(1, nameof(SendChat), msg);
        PlaySwitch();
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

    private void LayoutHud()
    {
        var size = GetViewport().GetVisibleRect().Size;
        if (topBar != null) topBar.Size = new Vector2(size.X, 25);
        if (playerListPanel != null) playerListPanel.Position = new Vector2(size.X - 220, 31);
        if (healthPanel != null) healthPanel.Position = new Vector2(size.X - 125, size.Y * 0.36f);
        if (inventoryPanel != null) inventoryPanel.Position = new Vector2(8, size.Y - 96);
        if (chatInput != null) chatInput.Position = new Vector2(8, size.Y - 30);
    }

    private void PlaySwitch()
    {
        if (switchSound?.Stream == null) return;
        switchSound.Stop();
        switchSound.Play();
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

    private static LaunchData ReadLaunchData(string[] args)
    {
        var launch = new LaunchData
        {
            GameId = ReadArg(args, "--game", "1"),
            BaseUrl = ReadArg(args, "--base-url", "http://localhost:3000"),
            Ticket = ReadArg(args, "--ticket", ""),
            ServerHost = ReadArg(args, "--server", "127.0.0.1"),
            ServerPort = ReadIntArg(args, "--port", 53640)
        };
        var joinJson = ReadArg(args, "--join-json", "");
        if (string.IsNullOrWhiteSpace(joinJson) || !File.Exists(joinJson)) return launch;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(joinJson));
            var root = doc.RootElement;
            launch.GameId = GetJsonString(root, "gameId", launch.GameId);
            launch.BaseUrl = GetJsonString(root, "baseUrl", launch.BaseUrl);
            launch.Ticket = GetJsonString(root, "ticket", launch.Ticket);
            launch.ServerHost = GetJsonString(root, "serverHost", launch.ServerHost);
            launch.ServerPort = GetJsonInt(root, "serverPort", launch.ServerPort);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Could not parse join json: {ex.Message}");
        }
        return launch;
    }

    private static string GetJsonString(JsonElement root, string key, string fallback)
    {
        if (!root.TryGetProperty(key, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? fallback;
        if (value.ValueKind == JsonValueKind.Number) return value.ToString();
        return fallback;
    }

    private static int GetJsonInt(JsonElement root, string key, int fallback)
    {
        if (!root.TryGetProperty(key, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return int.TryParse(GetJsonString(root, key, ""), out var parsed) ? parsed : fallback;
    }

    private sealed class LaunchData
    {
        public string GameId = "1";
        public string BaseUrl = "http://localhost:3000";
        public string Ticket = "";
        public string ServerHost = "127.0.0.1";
        public int ServerPort = 53640;
    }
}
