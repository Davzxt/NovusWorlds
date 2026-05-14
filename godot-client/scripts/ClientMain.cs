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
    private readonly Dictionary<string, Node3D> remotePlayers = new();
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
    private readonly HashSet<string> knownPlayers = new();
    private readonly Dictionary<string, string> playerNames = new();
    private double uiClock;
    private float cameraDistance = 12f;
    private bool rotatingCamera;
    private bool firstPerson;
    private bool shiftLock;
    private bool multiplayerConnected;
    private WebSocketPeer? wsPeer;
    private bool wsJoinSent;
    private string localNetworkId = "";
    private LaunchData launchData = new();
    private AudioStreamPlayer clickSound = null!;
    private Texture2D? arrowCursor;
    private Texture2D? dragCursor;
    private TextureRect? centerReticle;
    private CanvasLayer? loadingLayer;
    private Label? loadingLabel;
    private ProgressBar? loadingProgress;
    private Node3D skyboxRoot = null!;
    private Control topBar = null!;
    private Control playerListPanel = null!;
    private Control healthPanel = null!;
    private Control inventoryPanel = null!;
    private Control chatPanel = null!;
    private readonly List<Button> inventorySlots = new();
    private int selectedInventorySlot = -1;
    private double respawnCooldown;
    private double voidGrace;
    private float voidKillY = -90f;

    public override async void _Ready()
    {
        SetupInput();
        SetupAssets();
        ShowLoading("Bricks: 0    Connectors: 0", 0.08f);
        var args = OS.GetCmdlineArgs();
        var launch = ReadLaunchData(args);
        launchData = launch;
        SetLoading("Bricks: 0    Connectors: 0", 0.22f);
        try { map = await NovusApi.LoadPlace(launch.BaseUrl, launch.GameId, launch.Ticket); }
        catch (Exception ex) { GD.PushWarning($"Using local baseplate: {ex.Message}"); NovusApi.EnsurePlayable(map); }
        voidKillY = ComputeVoidKillY();
        SetLoading($"Bricks: {map.Objects.Count}    Connectors: 0", 0.58f);
        AddChild(MapBuilder.Build(map));
        SetupLighting();
        SpawnPlayer();
        SetLoading($"Bricks: {map.Objects.Count}    Connectors: 0", 0.76f);
        try { localAvatar = await NovusApi.LoadAvatar(launch.BaseUrl, launch.Ticket); player.SetAvatar(localAvatar); player.SetDisplayName(localAvatar.Username); }
        catch (Exception ex) { GD.PushWarning($"Avatar not loaded: {ex.Message}"); }
        SetLoading($"Bricks: {map.Objects.Count}    Connectors: 0", 0.92f);
        SetupDesktopHud();
        if (IsMobileDevice()) SetupMobileHud();
        ConnectMultiplayer(launch);
        await ToSignal(GetTree().CreateTimer(0.28), SceneTreeTimer.SignalName.Timeout);
        HideLoading();
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
            if (mouseButton.ButtonIndex == MouseButton.Left || mouseButton.ButtonIndex == MouseButton.Right)
                UpdateCursor(mouseButton.Pressed);
            if (mouseButton.ButtonIndex == MouseButton.Left && mouseButton.Pressed) PlayClick();
            if (mouseButton.ButtonIndex == MouseButton.Right) rotatingCamera = mouseButton.Pressed;
            if (mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.WheelUp)
            {
                cameraDistance = Mathf.Max(6f, cameraDistance - 1.2f);
                firstPerson = cameraDistance <= 6.05f;
                UpdateCameraMode();
                PlayClick();
            }
            if (mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.WheelDown)
            {
                cameraDistance = Mathf.Min(28f, cameraDistance + 1.2f);
                if (cameraDistance > 6.05f) firstPerson = false;
                UpdateCameraMode();
                PlayClick();
            }
        }
        else if (ev is InputEventMouseMotion motion && (rotatingCamera || firstPerson || shiftLock) && chatInput?.HasFocus() != true)
        {
            yaw -= motion.Relative.X * 0.18f;
            pitch = Mathf.Clamp(pitch - motion.Relative.Y * 0.18f, firstPerson ? -72f : -18f, firstPerson ? 72f : 68f);
        }
        else if (ev is InputEventKey key && key.Pressed)
        {
            if (chatInput?.HasFocus() == true && key.Keycode != Key.Escape) return;
            if (key.Keycode == Key.Slash)
            {
                firstPerson = false;
                shiftLock = false;
                UpdateCameraMode();
                chatInput?.GrabFocus();
                PlayClick();
            }
            if (key.Keycode == Key.Escape)
            {
                chatInput?.ReleaseFocus();
                firstPerson = false;
                shiftLock = false;
                UpdateCameraMode();
            }
            if (key.Keycode == Key.V)
            {
                firstPerson = !firstPerson;
                if (firstPerson) cameraDistance = 0.4f;
                else cameraDistance = Mathf.Max(cameraDistance, 8f);
                UpdateCameraMode();
                PlayClick();
            }
            if (key.Keycode == Key.Shift)
            {
                shiftLock = !shiftLock;
                UpdateCameraMode();
                PlayClick();
            }
            if (key.Keycode >= Key.Key1 && key.Keycode <= Key.Key5)
                SelectInventorySlot((int)(key.Keycode - Key.Key1));
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
                pitch = Mathf.Clamp(pitch - drag.Relative.Y * 0.18f, firstPerson ? -72f : -18f, firstPerson ? 72f : 68f);
            }
        }
    }

    public override void _Process(double delta)
    {
        if (player != null)
        {
            var yawRad = Mathf.DegToRad(yaw);
            var pitchRad = Mathf.DegToRad(pitch);
            if (firstPerson)
            {
                var lookDir = new Vector3(Mathf.Sin(yawRad) * Mathf.Cos(pitchRad), Mathf.Sin(pitchRad), Mathf.Cos(yawRad) * Mathf.Cos(pitchRad)).Normalized();
                var eye = player.GlobalPosition + Vector3.Up * 3.62f;
                camera.GlobalPosition = eye;
                camera.LookAt(eye + lookDir);
            }
            else
            {
                var horizontal = Mathf.Cos(pitchRad) * cameraDistance;
                var offset = new Vector3(Mathf.Sin(yawRad) * horizontal, Mathf.Sin(pitchRad) * cameraDistance + 1.2f, Mathf.Cos(yawRad) * horizontal);
                camera.GlobalPosition = camera.GlobalPosition.Lerp(player.GlobalPosition + offset, 0.35f);
                camera.LookAt(player.GlobalPosition + Vector3.Up * 2f);
            }
            player.SetLocalVisualHidden(firstPerson);
            if (skyboxRoot != null) skyboxRoot.GlobalPosition = camera.GlobalPosition;
            if (respawnCooldown > 0) respawnCooldown -= delta;
            if (voidGrace > 0) voidGrace -= delta;
            if (voidGrace <= 0 && player.GlobalPosition.Y < voidKillY) RespawnPlayer(true);
            netClock += delta;
            PollWebSocket();
            if (multiplayerConnected && wsPeer != null && netClock >= 0.05)
            {
                netClock = 0;
                SendWsMove();
            }
            else if (multiplayerConnected && Multiplayer.GetUniqueId() != 1 && netClock >= 0.05)
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
        arrowCursor = GD.Load<Texture2D>("res://assets/ui/ArrowCursor.png");
        dragCursor = GD.Load<Texture2D>("res://assets/ui/DragCursor.png");
        UpdateCursor(false);
        clickSound = new AudioStreamPlayer { Stream = GD.Load<AudioStream>("res://assets/audio/clickfast.wav"), VolumeDb = -4f };
        AddChild(clickSound);
    }

    private void UpdateCursor(bool dragging)
    {
        var cursor = dragging ? dragCursor : arrowCursor;
        if (cursor == null) return;
        Input.SetCustomMouseCursor(cursor, Input.CursorShape.Arrow, Vector2.Zero);
        Input.SetCustomMouseCursor(cursor, Input.CursorShape.PointingHand, Vector2.Zero);
        Input.SetCustomMouseCursor(cursor, Input.CursorShape.Drag, Vector2.Zero);
    }

    private void UpdateCameraMode()
    {
        if (firstPerson) shiftLock = false;
        if (centerReticle != null) centerReticle.Visible = firstPerson || shiftLock;
        Input.MouseMode = firstPerson || shiftLock ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
    }

    private void ShowLoading(string text, float progress)
    {
        loadingLayer = new CanvasLayer { Name = "LoadingOverlay", Layer = 100 };
        AddChild(loadingLayer);
        var root = new Control();
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        loadingLayer.AddChild(root);
        root.AddChild(new ColorRect { Color = new Color(0, 0, 0, 0.72f), AnchorRight = 1, AnchorBottom = 1 });
        var panel = Panel(Vector2.Zero, new Vector2(420, 118), new Color(0.48f, 0.47f, 0.42f, 0.76f));
        panel.AnchorLeft = 0.5f;
        panel.AnchorTop = 0.5f;
        panel.AnchorRight = 0.5f;
        panel.AnchorBottom = 0.5f;
        panel.OffsetLeft = -210;
        panel.OffsetTop = -59;
        panel.OffsetRight = 210;
        panel.OffsetBottom = 59;
        root.AddChild(panel);
        loadingLabel = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(24, 30),
            Size = new Vector2(372, 32),
            Modulate = Colors.White
        };
        panel.AddChild(loadingLabel);
        loadingProgress = new ProgressBar
        {
            Position = new Vector2(40, 76),
            Size = new Vector2(340, 14),
            MinValue = 0,
            MaxValue = 1,
            Value = progress,
            ShowPercentage = false
        };
        panel.AddChild(loadingProgress);
    }

    private void SetLoading(string text, float progress)
    {
        if (loadingLabel != null) loadingLabel.Text = text;
        if (loadingProgress != null) loadingProgress.Value = progress;
    }

    private void HideLoading()
    {
        if (loadingLayer == null) return;
        loadingLayer.QueueFree();
        loadingLayer = null;
        loadingLabel = null;
        loadingProgress = null;
    }

    private float ComputeVoidKillY()
    {
        var killY = Mathf.Min(map.Spawn.Y - 80f, -80f);
        foreach (var part in map.Objects)
            killY = Mathf.Min(killY, part.Position.Y - part.Size.Y * 0.5f - 35f);
        return Mathf.Min(killY, -55f);
    }

    private void ConnectMultiplayer(LaunchData launch)
    {
        if (ConnectWebSocketMultiplayer(launch)) return;
        ConnectEnetMultiplayer(launch.ServerHost, launch.ServerPort);
    }

    private bool ConnectWebSocketMultiplayer(LaunchData launch)
    {
        try
        {
            var baseUri = new Uri(launch.BaseUrl);
            var scheme = baseUri.Scheme == "https" ? "wss" : "ws";
            var path = $"/ws/game/{Uri.EscapeDataString(launch.GameId)}";
            var uri = $"{scheme}://{baseUri.Host}{(baseUri.IsDefaultPort ? "" : ":" + baseUri.Port)}{path}";
            wsPeer = new WebSocketPeer();
            var err = wsPeer.ConnectToUrl(uri);
            if (err != Error.Ok)
            {
                wsPeer = null;
                return false;
            }
            localNetworkId = localAvatar.UserId > 0 ? localAvatar.UserId.ToString() : GuestKey();
            GD.Print("Connecting to Novus WebSocket server: " + uri);
            return true;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"WebSocket multiplayer unavailable: {ex.Message}");
            wsPeer = null;
            return false;
        }
    }

    private void ConnectEnetMultiplayer(string host, int port)
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
            multiplayerConnected = true;
            localNetworkId = Multiplayer.GetUniqueId().ToString();
            GD.Print("Connected to Novus Godot server");
            RpcId(1, nameof(RegisterPlayer), localAvatar.Username);
        };
        Multiplayer.ConnectionFailed += () => { multiplayerConnected = false; GD.PushWarning("Could not connect to Novus Godot server"); };
        Multiplayer.ServerDisconnected += () => { multiplayerConnected = false; GD.PushWarning("Disconnected from Novus Godot server"); };
    }

    private void PollWebSocket()
    {
        if (wsPeer == null) return;
        wsPeer.Poll();
        var state = wsPeer.GetReadyState();
        if (state == WebSocketPeer.State.Open)
        {
            if (!wsJoinSent)
            {
                wsJoinSent = true;
                multiplayerConnected = true;
                SendWsJoin();
            }
            while (wsPeer.GetAvailablePacketCount() > 0)
                HandleWsMessage(wsPeer.GetPacket().GetStringFromUtf8());
        }
        else if (state == WebSocketPeer.State.Closed)
        {
            if (multiplayerConnected) AddChatLine("Multiplayer desconectado.");
            multiplayerConnected = false;
            wsPeer = null;
        }
    }

    private void SendWsJoin()
    {
        if (wsPeer == null || player == null) return;
        var payload = new Dictionary<string, object>
        {
            ["type"] = "join",
            ["gameId"] = launchData.GameId,
            ["userId"] = localAvatar.UserId,
            ["guestKey"] = localNetworkId,
            ["username"] = localAvatar.Username,
            ["avatarData"] = AvatarToWire(localAvatar),
            ["position"] = Vec(player.GlobalPosition)
        };
        wsPeer.SendText(JsonSerializer.Serialize(payload));
        playerNames[localNetworkId] = localAvatar.Username;
        AddChatLine("Multiplayer conectado ao servidor do site.");
    }

    private void SendWsMove()
    {
        if (wsPeer == null || player == null || wsPeer.GetReadyState() != WebSocketPeer.State.Open) return;
        wsPeer.SendText(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["type"] = "move",
            ["position"] = Vec(player.GlobalPosition),
            ["rotation"] = Vec(player.RotationDegrees),
            ["animation"] = player.CurrentAnimation
        }));
    }

    private void SendWsChat(string msg)
    {
        if (wsPeer == null || wsPeer.GetReadyState() != WebSocketPeer.State.Open) return;
        wsPeer.SendText(JsonSerializer.Serialize(new Dictionary<string, object> { ["type"] = "chat", ["message"] = msg }));
    }

    private void HandleWsMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = GetJsonString(root, "type", "");
            if (type == "world_state" && root.TryGetProperty("players", out var players) && players.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in players.EnumerateArray()) ApplyWsPlayer(p);
                UpdatePlayerList();
            }
            else if (type == "player_join" && root.TryGetProperty("player", out var joined))
            {
                ApplyWsPlayer(joined);
                var name = GetJsonString(joined, "username", "Player");
                if (GetJsonString(joined, "id", "") != localNetworkId) AddChatLine($"{name} entrou no jogo.");
            }
            else if (type == "player_leave")
            {
                RemoveRemotePlayer(GetJsonString(root, "playerId", ""));
            }
            else if (type == "chat_broadcast")
            {
                var id = GetJsonString(root, "playerId", "");
                var message = Moderate(GetJsonString(root, "message", ""));
                var from = GetJsonString(root, "from", PlayerName(id));
                if (id == localNetworkId) return;
                playerNames[id] = from;
                AddChatLine($"{from}: {message}");
                if (remotePlayers.TryGetValue(id, out var remote) && remote is R6Character r6) r6.ShowChatBubble(message);
            }
            else if (type == "error")
            {
                AddChatLine("Multiplayer: " + GetJsonString(root, "message", "erro"));
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Bad websocket message: {ex.Message}");
        }
    }

    private void ApplyWsPlayer(JsonElement p)
    {
        var id = GetJsonString(p, "id", "");
        if (string.IsNullOrWhiteSpace(id) || id == localNetworkId) return;
        var username = GetJsonString(p, "username", $"Player{id}");
        var position = p.TryGetProperty("position", out var pos) ? ReadVector(pos, Vector3.Zero) : Vector3.Zero;
        var rotation = p.TryGetProperty("rotation", out var rot) ? ReadVector(rot, Vector3.Zero) : Vector3.Zero;
        var animation = GetJsonString(p, "animation", "idle");
        var created = false;
        if (!remotePlayers.TryGetValue(id, out var remote))
        {
            remote = CreateRemotePlayer(id);
            remotePlayers[id] = remote;
            AddChild(remote);
            created = true;
        }
        remote.GlobalPosition = remote.GlobalPosition.Lerp(position, 0.35f);
        remote.RotationDegrees = rotation;
        if (remote is R6Character r6)
        {
            if (created && p.TryGetProperty("avatar", out var avatarData) && avatarData.ValueKind == JsonValueKind.Object)
                r6.SetAvatar(NovusApi.ParseAvatar(avatarData));
            r6.SetDisplayName(username);
            r6.SetRemoteAnimation(animation);
        }
        knownPlayers.Add(id);
        playerNames[id] = username;
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    public void SubmitState(Vector3 position, Vector3 rotation, string animation) {}

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void RegisterPlayer(string username) {}

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    public void ReceiveState(long id, Vector3 position, Vector3 rotation, string animation)
    {
        var key = id.ToString();
        if (key == localNetworkId) return;
        if (!remotePlayers.TryGetValue(key, out var remote))
        {
            remote = CreateRemotePlayer(key);
            remotePlayers[key] = remote;
            AddChild(remote);
        }
        remote.GlobalPosition = remote.GlobalPosition.Lerp(position, 0.35f);
        remote.RotationDegrees = rotation;
        if (remote is R6Character r6) r6.SetRemoteAnimation(animation);
        knownPlayers.Add(key);
        if (!playerNames.ContainsKey(key)) playerNames[key] = $"Player{id}";
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveChat(long id, string message)
    {
        var key = id.ToString();
        if (key == localNetworkId) return;
        var clean = Moderate(message);
        AddChatLine($"{PlayerName(key)}: {clean}");
        if (remotePlayers.TryGetValue(key, out var remote) && remote is R6Character r6) r6.ShowChatBubble(clean);
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
        var key = id.ToString();
        knownPlayers.Add(key);
        playerNames[key] = username;
        AddChatLine($"{username} entrou no jogo.");
        UpdatePlayerList();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void PlayerLeft(long id)
    {
        RemoveRemotePlayer(id.ToString());
    }

    private void RemoveRemotePlayer(string id)
    {
        if (!remotePlayers.TryGetValue(id, out var remote)) return;
        remote.QueueFree();
        remotePlayers.Remove(id);
        knownPlayers.Remove(id);
        playerNames.Remove(id);
        UpdatePlayerList();
    }

    private static Node3D CreateRemotePlayer(string id)
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
        AddSkyboxCube(panorama);
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

        centerReticle = new TextureRect
        {
            Texture = GD.Load<Texture2D>("res://assets/ui/GunCursor.png"),
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            Size = new Vector2(32, 32)
        };
        root.AddChild(centerReticle);

        topBar = new Control { Name = "Classic2011Controls", Size = new Vector2(190, 86) };
        root.AddChild(topBar);
        AddTopButton("Lock Mouse", 0, 0, 146, () =>
        {
            shiftLock = !shiftLock;
            UpdateCameraMode();
            PlayClick();
        });
        AddTopButton("Leave", 0, 42, 58, () => { PlayClick(); GetTree().Quit(); });
        AddTopButton("Menu", 68, 42, 44, () => { AddChatLine("WASD move, right mouse rotates camera, mouse wheel zooms, / opens chat."); PlayClick(); });
        AddTopButton("Gear", 122, 42, 44, () => { PlayClick(); });

        healthPanel = Panel(Vector2.Zero, new Vector2(176, 24), new Color(0f, 0f, 0f, 0.68f));
        root.AddChild(healthPanel);
        healthFill = new ColorRect { Position = new Vector2(4, 5), Size = new Vector2(168, 12), Color = new Color(0.05f, 0.72f, 0.05f) };
        healthPanel.AddChild(healthFill);
        healthLabel = new Label { Text = "HEALTH", Position = new Vector2(116, 3), Size = new Vector2(54, 16), HorizontalAlignment = HorizontalAlignment.Right, Modulate = Colors.White };
        healthPanel.AddChild(healthLabel);

        playerListPanel = Panel(new Vector2(1048, 8), new Vector2(336, 76), new Color(0.12f, 0.16f, 0.22f, 0.58f));
        root.AddChild(playerListPanel);
        playerListPanel.AddChild(new ColorRect { Position = new Vector2(0, 30), Size = new Vector2(336, 20), Color = new Color(0.72f, 0.18f, 0.18f, 0.82f) });
        playerListPanel.AddChild(new Label { Text = "Players", Position = new Vector2(8, 5), Size = new Vector2(220, 24), Modulate = Colors.White });
        playerList = new Label { Text = "", Position = new Vector2(20, 31), Size = new Vector2(292, 42), Modulate = new Color(1f, 0.08f, 0.04f) };
        playerListPanel.AddChild(playerList);

        inventoryPanel = Panel(new Vector2(8, 625), new Vector2(300, 76), new Color(0f, 0f, 0f, 0.38f));
        root.AddChild(inventoryPanel);
        BuildInventory();

        chatPanel = new Control { Position = new Vector2(8, 28), Size = new Vector2(520, 136) };
        root.AddChild(chatPanel);
        chatLog = new RichTextLabel { Position = Vector2.Zero, Size = new Vector2(520, 112), BbcodeEnabled = false, ScrollActive = true, Modulate = Colors.White };
        chatPanel.AddChild(chatLog);
        chatInput = new LineEdit { PlaceholderText = "To chat click here or press the / key", Position = new Vector2(8, 690), Size = new Vector2(520, 24) };
        chatInput.TextSubmitted += SendChatMessage;
        root.AddChild(chatInput);
        AddChatLine("Bem-vindo ao Novus Worlds.");
        LayoutHud();
    }

    private void AddTopButton(string text, float x, float y, float width, Action action)
    {
        var button = new Button { Text = text, Position = new Vector2(x, y), Size = new Vector2(width, 34), FocusMode = Control.FocusModeEnum.None };
        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.08f, 0.08f, 0.48f),
            BorderColor = new Color(0.78f, 0.78f, 0.78f, 0.42f),
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3
        };
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = new Color(0.16f, 0.16f, 0.16f, 0.68f);
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", hover);
        button.AddThemeColorOverride("font_color", Colors.White);
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
            Size = new Vector2(130, 72),
            FocusMode = Control.FocusModeEnum.None
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
        chatInput.ReleaseFocus();
        UpdateCameraMode();
        if (msg.Length == 0) return;
        AddChatLine($"{localAvatar.Username}: {msg}");
        player.ShowChatBubble(msg);
        if (wsPeer != null) SendWsChat(msg);
        if (Multiplayer.MultiplayerPeer != null && Multiplayer.GetUniqueId() != 1) RpcId(1, nameof(SendChat), msg);
        PlayClick();
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
        var text = localAvatar.Username + "\n";
        foreach (var id in knownPlayers)
            if (id != localNetworkId) text += PlayerName(id) + "\n";
        playerList.Text = text;
    }

    private void LayoutHud()
    {
        var size = GetViewport().GetVisibleRect().Size;
        if (topBar != null) topBar.Position = new Vector2(4, size.Y - 88);
        if (playerListPanel != null) playerListPanel.Position = new Vector2(size.X - 340, 8);
        if (healthPanel != null) healthPanel.Position = new Vector2(size.X * 0.5f - 88, size.Y - 31);
        if (inventoryPanel != null) inventoryPanel.Position = new Vector2(size.X * 0.5f - inventoryPanel.Size.X * 0.5f, size.Y - 86);
        if (chatInput != null) chatInput.Position = new Vector2(4, size.Y - 25);
        if (centerReticle != null) centerReticle.Position = size * 0.5f - centerReticle.Size * 0.5f;
    }

    private void BuildInventory()
    {
        if (inventoryPanel == null) return;
        inventorySlots.Clear();
        foreach (var child in inventoryPanel.GetChildren()) child.QueueFree();
        var items = localAvatar.Items;
        inventoryPanel.Visible = items.Count > 0;
        if (!inventoryPanel.Visible) return;
        var count = Mathf.Min(5, items.Count);
        inventoryPanel.Size = new Vector2(8 + count * 58, 58);
        for (var i = 0; i < count; i++)
        {
            var item = items[i];
            var button = new Button
            {
                Text = (i + 1).ToString(),
                Position = new Vector2(5 + i * 58, 5),
                Size = new Vector2(52, 52),
                TooltipText = item.Name,
                FocusMode = Control.FocusModeEnum.None
            };
            var normal = new StyleBoxFlat
            {
                BgColor = new Color(0.02f, 0.02f, 0.025f, 0.86f),
                BorderColor = new Color(0.68f, 0.68f, 0.68f, 0.72f),
                BorderWidthBottom = 2,
                BorderWidthLeft = 2,
                BorderWidthRight = 2,
                BorderWidthTop = 2,
                CornerRadiusBottomLeft = 2,
                CornerRadiusBottomRight = 2,
                CornerRadiusTopLeft = 2,
                CornerRadiusTopRight = 2
            };
            var selected = (StyleBoxFlat)normal.Duplicate();
            selected.BorderColor = new Color(1f, 0.04f, 0.03f);
            selected.BgColor = new Color(0f, 0f, 0f, 0.94f);
            button.AddThemeStyleboxOverride("normal", normal);
            button.AddThemeStyleboxOverride("hover", selected);
            button.AddThemeStyleboxOverride("pressed", selected);
            button.AddThemeColorOverride("font_color", Colors.White);
            var slot = i;
            button.Pressed += () => SelectInventorySlot(slot);
            inventoryPanel.AddChild(button);
            inventorySlots.Add(button);
        }
        SelectInventorySlot(0, false);
    }

    private void SelectInventorySlot(int slot, bool playSound = true)
    {
        if (slot < 0 || slot >= inventorySlots.Count) return;
        selectedInventorySlot = slot;
        for (var i = 0; i < inventorySlots.Count; i++)
        {
            inventorySlots[i].Modulate = Colors.White;
            if (inventorySlots[i].GetThemeStylebox("normal") is StyleBoxFlat style)
            {
                var next = (StyleBoxFlat)style.Duplicate();
                next.BorderColor = i == selectedInventorySlot ? new Color(1f, 0.03f, 0.02f) : new Color(0.68f, 0.68f, 0.68f, 0.72f);
                inventorySlots[i].AddThemeStyleboxOverride("normal", next);
            }
        }
        if (playSound) PlayClick();
    }

    private void RespawnPlayer(bool killed, bool force = false)
    {
        if (player == null) return;
        if (!force && respawnCooldown > 0) return;
        respawnCooldown = 1.25;
        voidGrace = 1.75;
        player.MobileMove = Vector2.Zero;
        player.Respawn(map.Spawn + Vector3.Up * 4f);
        if (killed) AddChatLine($"{localAvatar.Username} caiu no void.");
        if (healthFill != null)
        {
            healthFill.Size = new Vector2(killed ? 0 : 168, 12);
            GetTree().CreateTimer(0.3).Timeout += () =>
            {
                if (IsInstanceValid(healthFill)) healthFill.Size = new Vector2(168, 12);
            };
        }
        PlayClick();
    }

    private void AddSkyboxCube(Texture2D texture)
    {
        if (texture == null) return;
        skyboxRoot = new Node3D { Name = "ClassicSkybox" };
        var material = new StandardMaterial3D
        {
            AlbedoTexture = texture,
            AlbedoColor = Colors.White,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
        };
        skyboxRoot.AddChild(new MeshInstance3D
        {
            Name = "SkyboxCube",
            Mesh = new BoxMesh { Size = new Vector3(900, 900, 900) },
            MaterialOverride = material
        });
        AddChild(skyboxRoot);
    }

    private void PlayClick()
    {
        if (clickSound?.Stream == null) return;
        clickSound.Stop();
        clickSound.Play();
    }

    private string PlayerName(string id) => playerNames.TryGetValue(id, out var name) ? name : $"Player{id}";

    private static Dictionary<string, object> Vec(Vector3 value)
    {
        return new Dictionary<string, object> { ["x"] = value.X, ["y"] = value.Y, ["z"] = value.Z };
    }

    private static Vector3 ReadVector(JsonElement value, Vector3 fallback)
    {
        return new Vector3(GetJsonFloat(value, "x", fallback.X), GetJsonFloat(value, "y", fallback.Y), GetJsonFloat(value, "z", fallback.Z));
    }

    private static Dictionary<string, object> AvatarToWire(NovusAvatar avatar)
    {
        var items = new List<Dictionary<string, object>>();
        foreach (var item in avatar.Items)
        {
            items.Add(new Dictionary<string, object>
            {
                ["id"] = item.Id,
                ["name"] = item.Name,
                ["type"] = item.Type,
                ["modelUrl"] = item.ModelUrl,
                ["textureUrl"] = item.TextureUrl,
                ["assetUrl"] = item.AssetUrl,
                ["thumbnailUrl"] = item.ThumbnailUrl,
                ["hatTransform"] = new Dictionary<string, object>
                {
                    ["position"] = Vec(item.HatPosition),
                    ["rotation"] = Vec(item.HatRotation),
                    ["scale"] = Vec(item.HatScale)
                }
            });
        }
        return new Dictionary<string, object>
        {
            ["userId"] = avatar.UserId,
            ["username"] = avatar.Username,
            ["face"] = avatar.Face,
            ["colors"] = new Dictionary<string, object>
            {
                ["head"] = ColorToHex(avatar.HeadColor),
                ["torso"] = ColorToHex(avatar.TorsoColor),
                ["arms"] = ColorToHex(avatar.ArmsColor),
                ["legs"] = ColorToHex(avatar.LegsColor)
            },
            ["items"] = items
        };
    }

    private static string ColorToHex(Color color)
    {
        return $"#{(int)(color.R * 255):X2}{(int)(color.G * 255):X2}{(int)(color.B * 255):X2}";
    }

    private static string GuestKey()
    {
        var path = Path.Combine(OS.GetUserDataDir(), "guest-key.txt");
        if (File.Exists(path)) return File.ReadAllText(path).Trim();
        var key = "guest_" + Guid.NewGuid().ToString("N");
        File.WriteAllText(path, key);
        return key;
    }

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

    private static float GetJsonFloat(JsonElement root, string key, float fallback)
    {
        if (!root.TryGetProperty(key, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var number)) return number;
        return float.TryParse(GetJsonString(root, key, ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
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
