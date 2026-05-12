using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

public partial class StudioMain : Node3D
{
    private enum ToolMode { Select, Move, Rotate, Scale }

    private NovusMap map = new();
    private Node3D workspace = null!;
    private Camera3D camera = null!;
    private CanvasLayer ui = null!;
    private Control rootUi = null!;
    private Tree explorer = null!;
    private VBoxContainer properties = null!;
    private TextEdit scriptEditor = null!;
    private RichTextLabel output = null!;
    private Label status = null!;
    private MeshInstance3D? selectionBox;
    private FileDialog openDialog = null!;
    private FileDialog importDialog = null!;

    private ToolMode mode = ToolMode.Select;
    private int selectedPart = -1;
    private int selectedScript = -1;
    private string baseUrl = "http://localhost:3000";
    private string ticket = "";
    private bool cameraRotating;
    private bool updatingProperties;
    private float yaw = -45f;
    private float pitch = -32f;

    public override async void _Ready()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var args = OS.GetCmdlineArgs();
        baseUrl = ReadArg(args, "--base-url", baseUrl);
        ticket = ReadArg(args, "--ticket", "");
        try
        {
            var projectJson = ReadArg(args, "--project-json", "");
            if (!string.IsNullOrWhiteSpace(projectJson) && File.Exists(projectJson))
            {
                LoadProjectJson(File.ReadAllText(projectJson));
                Log("Projeto carregado do launcher.");
            }
            else
            {
                map = ticket.Length > 0 ? await NovusApi.LoadStudioProject(baseUrl, ticket) : await NovusApi.LoadPlace(baseUrl, ReadArg(args, "--game", "1"));
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Using local studio baseplate: {ex.Message}");
            NovusApi.EnsurePlayable(map);
        }

        if (map.Scripts.Count == 0) map.Scripts.Add(DefaultScript());
        SetupScene();
        SetupUi();
        RebuildWorkspace();
        RebuildExplorer();
        SelectPart(0);
    }

    public override void _Input(InputEvent ev)
    {
        if (ev is InputEventMouseButton mouse)
        {
            if (mouse.ButtonIndex == MouseButton.Right) cameraRotating = mouse.Pressed;
            if (mouse.Pressed && mouse.ButtonIndex == MouseButton.Left && !IsPointerOverUi(mouse.Position)) PickPart(mouse.Position);
            if (mouse.Pressed && mouse.ButtonIndex == MouseButton.WheelUp) camera.Position += -camera.GlobalBasis.Z * 2f;
            if (mouse.Pressed && mouse.ButtonIndex == MouseButton.WheelDown) camera.Position += camera.GlobalBasis.Z * 2f;
        }
        else if (ev is InputEventMouseMotion motion && cameraRotating)
        {
            yaw -= motion.Relative.X * 0.18f;
            pitch = Mathf.Clamp(pitch - motion.Relative.Y * 0.18f, -82f, 82f);
            camera.RotationDegrees = new Vector3(pitch, yaw, 0);
        }
        else if (ev is InputEventKey key && key.Pressed)
        {
            if (scriptEditor?.HasFocus() == true) return;
            if (key.Keycode == Key.Delete) DeleteSelected();
            if (key.CtrlPressed && key.Keycode == Key.D) DuplicateSelected();
            if (key.CtrlPressed && key.Keycode == Key.S) SaveProject(false);
            if (key.Keycode == Key.F) FocusSelected();
            if (key.Keycode == Key.Key1) SetMode(ToolMode.Select);
            if (key.Keycode == Key.Key2) SetMode(ToolMode.Move);
            if (key.Keycode == Key.Key3) SetMode(ToolMode.Rotate);
            if (key.Keycode == Key.Key4) SetMode(ToolMode.Scale);
            if (key.Keycode == Key.Up || key.Keycode == Key.Down || key.Keycode == Key.Left || key.Keycode == Key.Right || key.Keycode == Key.Pageup || key.Keycode == Key.Pagedown)
                NudgeSelection(key.Keycode, key.ShiftPressed ? 5f : 1f);
        }
    }

    public override void _Process(double delta)
    {
        var speed = Input.IsKeyPressed(Key.Shift) ? 42f : 18f;
        var move = Vector3.Zero;
        if (Input.IsKeyPressed(Key.W)) move.Z -= 1;
        if (Input.IsKeyPressed(Key.S)) move.Z += 1;
        if (Input.IsKeyPressed(Key.A)) move.X -= 1;
        if (Input.IsKeyPressed(Key.D)) move.X += 1;
        if (Input.IsKeyPressed(Key.E)) move.Y += 1;
        if (Input.IsKeyPressed(Key.Q)) move.Y -= 1;
        if (move != Vector3.Zero) camera.Position += camera.Basis * move.Normalized() * speed * (float)delta;
    }

    private void SetupScene()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = map.SkyColor,
                AmbientLightEnergy = 0.75f
            }
        });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-45, -35, 0), LightEnergy = 1.8f, ShadowEnabled = true });
        camera = new Camera3D { Name = "StudioCamera", Position = new Vector3(22, 18, 22), RotationDegrees = new Vector3(pitch, yaw, 0), Current = true, Fov = 65f };
        AddChild(camera);
    }

    private void SetupUi()
    {
        ui = new CanvasLayer();
        AddChild(ui);
        rootUi = new Control { Name = "StudioUi" };
        rootUi.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ui.AddChild(rootUi);

        var toolbar = Panel(new Vector2(0, 0), new Vector2(1366, 34), new Color(0.76f, 0.76f, 0.76f, 0.95f));
        rootUi.AddChild(toolbar);
        var top = new HBoxContainer { Position = new Vector2(6, 4), Size = new Vector2(1220, 28) };
        toolbar.AddChild(top);
        AddButton(top, "Select", () => SetMode(ToolMode.Select));
        AddButton(top, "Move", () => SetMode(ToolMode.Move));
        AddButton(top, "Rotate", () => SetMode(ToolMode.Rotate));
        AddButton(top, "Scale", () => SetMode(ToolMode.Scale));
        AddButton(top, "Part", () => AddPart("Part"));
        AddButton(top, "Spawn", AddSpawn);
        AddButton(top, "Script", AddScript);
        AddButton(top, "Run", RunLuauPreview);
        AddButton(top, "Save", () => SaveProject(false));
        AddButton(top, "Publish", () => SaveProject(true));
        AddButton(top, "Open .nwm", () => openDialog.PopupCentered(new Vector2I(720, 520)));
        AddButton(top, "Export .nwm", ExportProjectFile);

        var toolbox = Panel(new Vector2(8, 44), new Vector2(210, 515), new Color(0.88f, 0.9f, 0.92f, 0.96f));
        rootUi.AddChild(toolbox);
        var tools = new VBoxContainer { Position = new Vector2(8, 8), Size = new Vector2(194, 500) };
        toolbox.AddChild(tools);
        tools.AddChild(Header("Toolbox"));
        AddButton(tools, "Block", () => AddPart("Part"));
        AddButton(tools, "Sphere", () => AddPart("Sphere"));
        AddButton(tools, "Cylinder", () => AddPart("Cylinder"));
        AddButton(tools, "Wedge", () => AddPart("Wedge"));
        AddButton(tools, "Lava Brick", () => AddPresetPart("LavaBrick", new Vector3(0, 1, 0), new Vector3(8, 1, 8), new Color(1f, 0.18f, 0.08f), "Plastic"));
        AddButton(tools, "Jump Pad", () => AddPresetPart("JumpPad", new Vector3(0, 1, 0), new Vector3(8, 0.4f, 8), new Color(1f, 0.85f, 0.1f), "Plastic"));
        AddButton(tools, "Obby Starter", AddObbyStarter);
        AddButton(tools, "Classic Tower", AddClassicTower);
        AddButton(tools, "Clear Baseplate", ResetClassicBaseplate);
        tools.AddChild(new Label { Text = "Keys: 1-4 tools, Ctrl+S save, Ctrl+D duplicate, F focus, Delete remove.", AutowrapMode = TextServer.AutowrapMode.WordSmart });

        explorer = new Tree { Position = new Vector2(1046, 44), Size = new Vector2(312, 280), HideRoot = false };
        explorer.ItemSelected += OnExplorerSelected;
        rootUi.AddChild(explorer);

        var propPanel = Panel(new Vector2(1046, 330), new Vector2(312, 320), new Color(0.9f, 0.92f, 0.95f, 0.98f));
        rootUi.AddChild(propPanel);
        properties = new VBoxContainer { Position = new Vector2(8, 8), Size = new Vector2(296, 304) };
        propPanel.AddChild(properties);

        var scriptPanel = Panel(new Vector2(226, 560), new Vector2(812, 198), new Color(0.08f, 0.1f, 0.13f, 0.96f));
        rootUi.AddChild(scriptPanel);
        scriptEditor = new TextEdit
        {
            Position = new Vector2(8, 28),
            Size = new Vector2(796, 162),
            Text = "",
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            SyntaxHighlighter = null
        };
        scriptPanel.AddChild(new Label { Text = "Luau Script", Position = new Vector2(8, 5), Modulate = Colors.White });
        scriptPanel.AddChild(scriptEditor);
        scriptEditor.TextChanged += () =>
        {
            if (selectedScript >= 0 && selectedScript < map.Scripts.Count) map.Scripts[selectedScript].Source = scriptEditor.Text;
        };

        output = new RichTextLabel { Position = new Vector2(8, 654), Size = new Vector2(1350, 82), BbcodeEnabled = false, ScrollActive = true, Modulate = new Color(0.7f, 1f, 0.7f) };
        rootUi.AddChild(output);
        status = new Label { Text = "Novus Studio ready", Position = new Vector2(8, 738), Modulate = Colors.White };
        rootUi.AddChild(status);

        openDialog = new FileDialog { Access = FileDialog.AccessEnum.Filesystem, FileMode = FileDialog.FileModeEnum.OpenFile, Filters = new string[] { "*.nwm ; Novus World Model", "*.json ; JSON" } };
        openDialog.FileSelected += path => { LoadProjectJson(File.ReadAllText(path)); RebuildWorkspace(); RebuildExplorer(); Log("Projeto aberto: " + path); };
        rootUi.AddChild(openDialog);

        importDialog = new FileDialog { Access = FileDialog.AccessEnum.Filesystem, FileMode = FileDialog.FileModeEnum.OpenFile, Filters = new string[] { "*.nwm ; Novus World Model", "*.json ; JSON" } };
        rootUi.AddChild(importDialog);
    }

    private void RebuildWorkspace()
    {
        workspace?.QueueFree();
        workspace = MapBuilder.Build(map);
        AddChild(workspace);
        UpdateSelectionBox();
    }

    private void RebuildExplorer()
    {
        explorer.Clear();
        var root = explorer.CreateItem();
        root.SetText(0, "Novus Place");
        var workspaceItem = explorer.CreateItem(root);
        workspaceItem.SetText(0, "Workspace");
        workspaceItem.SetMetadata(0, "workspace");
        foreach (var part in map.Objects)
        {
            var item = explorer.CreateItem(workspaceItem);
            item.SetText(0, $"{IconFor(part)} {part.Name}");
            item.SetMetadata(0, "part:" + part.Id);
        }
        var scriptsItem = explorer.CreateItem(root);
        scriptsItem.SetText(0, "ServerScriptService");
        scriptsItem.SetMetadata(0, "scripts");
        foreach (var script in map.Scripts)
        {
            var item = explorer.CreateItem(scriptsItem);
            item.SetText(0, "Script " + script.Name);
            item.SetMetadata(0, "script:" + script.Id);
        }
        explorer.GetRoot()?.SetCollapsed(false);
    }

    private void RefreshProperties()
    {
        updatingProperties = true;
        foreach (var child in properties.GetChildren()) child.QueueFree();
        if (selectedPart >= 0 && selectedPart < map.Objects.Count)
        {
            var part = map.Objects[selectedPart];
            properties.AddChild(Header("Properties"));
            AddLineEdit(properties, "Name", part.Name, value => { part.Name = value; RebuildWorkspace(); RebuildExplorer(); });
            AddOption(properties, "Type", new[] { "Part", "Sphere", "Cylinder", "Wedge" }, part.Type, value => { part.Type = value; RebuildWorkspace(); RebuildExplorer(); });
            AddVector(properties, "Position", part.Position, value => { part.Position = value; RebuildWorkspace(); UpdateSelectionBox(); });
            AddVector(properties, "Rotation", part.Rotation, value => { part.Rotation = value; RebuildWorkspace(); UpdateSelectionBox(); });
            AddVector(properties, "Size", part.Size, value => { part.Size = new Vector3(Mathf.Max(0.1f, value.X), Mathf.Max(0.1f, value.Y), Mathf.Max(0.1f, value.Z)); RebuildWorkspace(); UpdateSelectionBox(); });
            AddColor(properties, "Color", part.Color, value => { part.Color = value; RebuildWorkspace(); });
            AddOption(properties, "Material", new[] { "Plastic", "Metal", "Wood", "Stone", "Grass", "Brick", "Glass" }, part.Material, value => { part.Material = value; RebuildWorkspace(); });
            AddCheck(properties, "Anchored", part.Anchored, value => { part.Anchored = value; RebuildWorkspace(); });
            AddCheck(properties, "CanCollide", part.CanCollide, value => { part.CanCollide = value; RebuildWorkspace(); });
            AddCheck(properties, "Locked", part.Locked, value => { part.Locked = value; });
            AddFloat(properties, "Transparency", part.Transparency, 0, 1, value => { part.Transparency = value; RebuildWorkspace(); });
            AddFloat(properties, "Reflectance", part.Reflectance, 0, 1, value => { part.Reflectance = value; RebuildWorkspace(); });
            AddButton(properties, "Make Parent Of Selected Script", () =>
            {
                if (selectedScript >= 0 && selectedScript < map.Scripts.Count) map.Scripts[selectedScript].ParentId = part.Id;
                RebuildExplorer();
            });
        }
        else if (selectedScript >= 0 && selectedScript < map.Scripts.Count)
        {
            var script = map.Scripts[selectedScript];
            properties.AddChild(Header("Script Properties"));
            AddLineEdit(properties, "Name", script.Name, value => { script.Name = value; RebuildExplorer(); });
            AddCheck(properties, "Disabled", script.Disabled, value => { script.Disabled = value; });
            AddButton(properties, "Snippet PlayerJoin", () => InsertSnippet("game.on(\"playerJoin\", function(player)\n  player:teleport(0, 8, 0)\nend)\n"));
            AddButton(properties, "Snippet Touch", () => InsertSnippet("game.on(\"partTouched\", function(player)\n  player:addScore(1)\nend)\n"));
            AddButton(properties, "Snippet Workspace", () => InsertSnippet("local p = workspace:FindFirstChild(\"Part\")\nif p then\n  p:setColor(\"#ff3333\")\n  p:move(0, 2, 0)\nend\n"));
        }
        else
        {
            properties.AddChild(Header("Place"));
            AddLineEdit(properties, "Name", map.Name, value => map.Name = value);
            AddLineEdit(properties, "Description", map.Description, value => map.Description = value);
            AddColor(properties, "Sky", map.SkyColor, value => map.SkyColor = value);
        }
        updatingProperties = false;
    }

    private void OnExplorerSelected()
    {
        var item = explorer.GetSelected();
        var meta = item?.GetMetadata(0).AsString() ?? "";
        if (meta.StartsWith("part:"))
        {
            var id = meta[5..];
            SelectPart(map.Objects.FindIndex(part => part.Id == id));
        }
        else if (meta.StartsWith("script:"))
        {
            var id = meta[7..];
            SelectScript(map.Scripts.FindIndex(script => script.Id == id));
        }
        else
        {
            selectedPart = -1;
            selectedScript = -1;
            RefreshProperties();
        }
    }

    private void PickPart(Vector2 screenPosition)
    {
        var from = camera.ProjectRayOrigin(screenPosition);
        var to = from + camera.ProjectRayNormal(screenPosition) * 2000f;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to));
        if (hit.Count == 0) return;
        if (!hit.TryGetValue("collider", out var colliderValue)) return;
        if (colliderValue.AsGodotObject() is not Node node) return;
        var id = FindNovusId(node);
        if (string.IsNullOrWhiteSpace(id)) return;
        SelectPart(map.Objects.FindIndex(part => part.Id == id));
    }

    private static string FindNovusId(Node node)
    {
        var current = node;
        while (current != null)
        {
            if (current.HasMeta("novus_id")) return current.GetMeta("novus_id").AsString();
            current = current.GetParent();
        }
        return "";
    }

    private void SelectPart(int index)
    {
        selectedPart = index;
        selectedScript = -1;
        scriptEditor.Text = "";
        if (selectedPart >= 0 && selectedPart < map.Objects.Count) status.Text = "Selected: " + map.Objects[selectedPart].Name;
        RefreshProperties();
        UpdateSelectionBox();
    }

    private void SelectScript(int index)
    {
        selectedScript = index;
        selectedPart = -1;
        if (selectedScript >= 0 && selectedScript < map.Scripts.Count)
        {
            scriptEditor.Text = map.Scripts[selectedScript].Source;
            status.Text = "Editing script: " + map.Scripts[selectedScript].Name;
        }
        RefreshProperties();
        UpdateSelectionBox();
    }

    private void UpdateSelectionBox()
    {
        selectionBox?.QueueFree();
        selectionBox = null;
        if (selectedPart < 0 || selectedPart >= map.Objects.Count) return;
        var part = map.Objects[selectedPart];
        selectionBox = new MeshInstance3D
        {
            Name = "SelectionBox",
            Position = part.Position,
            RotationDegrees = part.Rotation,
            Mesh = new BoxMesh { Size = part.Size + new Vector3(0.12f, 0.12f, 0.12f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.9f, 0.05f, 0.22f),
                EmissionEnabled = true,
                Emission = new Color(1f, 0.82f, 0.05f),
                EmissionEnergyMultiplier = 0.55f,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            }
        };
        AddChild(selectionBox);
    }

    private void AddPart(string type)
    {
        var part = new NovusPart
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = type,
            Name = type,
            Position = camera.Position + -camera.GlobalBasis.Z * 10f,
            Size = type == "Sphere" ? new Vector3(4, 4, 4) : new Vector3(4, 1, 4),
            Color = Colors.LightGray,
            Material = "Plastic"
        };
        map.Objects.Add(part);
        RebuildWorkspace();
        RebuildExplorer();
        SelectPart(map.Objects.Count - 1);
    }

    private void AddPresetPart(string name, Vector3 position, Vector3 size, Color color, string material)
    {
        map.Objects.Add(new NovusPart { Id = Guid.NewGuid().ToString("N"), Name = name, Position = position, Size = size, Color = color, Material = material, Anchored = true, CanCollide = true });
        RebuildWorkspace();
        RebuildExplorer();
        SelectPart(map.Objects.Count - 1);
    }

    private void AddSpawn()
    {
        map.Spawn = new Vector3(0, 4, 0);
        AddPresetPart("SpawnLocation", new Vector3(0, 0.25f, 0), new Vector3(6, 0.5f, 6), new Color(0.1f, 0.9f, 0.25f), "Plastic");
    }

    private void AddScript()
    {
        map.Scripts.Add(DefaultScript());
        RebuildExplorer();
        SelectScript(map.Scripts.Count - 1);
    }

    private void AddObbyStarter()
    {
        AddPresetPart("StartPad", new Vector3(0, 0.25f, 0), new Vector3(8, 0.5f, 8), new Color(0.1f, 0.9f, 0.25f), "Plastic");
        AddPresetPart("Jump1", new Vector3(12, 1.8f, 0), new Vector3(6, 1, 6), new Color(1f, 0.8f, 0.08f), "Plastic");
        AddPresetPart("Jump2", new Vector3(24, 3.3f, 0), new Vector3(6, 1, 6), new Color(1f, 0.25f, 0.16f), "Plastic");
        AddPresetPart("Finish", new Vector3(36, 4.8f, 0), new Vector3(8, 1, 8), new Color(0.2f, 0.45f, 1f), "Plastic");
    }

    private void AddClassicTower()
    {
        for (var y = 0; y < 6; y++)
            AddPresetPart("TowerFloor" + y, new Vector3(-20, 1 + y * 5, -12), new Vector3(14, 1, 14), new Color(0.45f, 0.25f, 0.12f), "Brick");
        AddPresetPart("TowerSpawn", new Vector3(-20, 4, -12), new Vector3(5, 0.5f, 5), new Color(0.1f, 0.9f, 0.25f), "Plastic");
    }

    private void ResetClassicBaseplate()
    {
        map.Objects.Clear();
        NovusApi.EnsurePlayable(map);
        map.Spawn = new Vector3(0, 4, 0);
        RebuildWorkspace();
        RebuildExplorer();
        SelectPart(0);
    }

    private void DuplicateSelected()
    {
        if (selectedPart < 0 || selectedPart >= map.Objects.Count) return;
        var src = map.Objects[selectedPart];
        map.Objects.Add(new NovusPart
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = src.Type,
            ParentId = src.ParentId,
            Name = src.Name + " Copy",
            Position = src.Position + new Vector3(2, 0, 2),
            Rotation = src.Rotation,
            Size = src.Size,
            Color = src.Color,
            Material = src.Material,
            Anchored = src.Anchored,
            CanCollide = src.CanCollide,
            Transparency = src.Transparency,
            Reflectance = src.Reflectance
        });
        RebuildWorkspace();
        RebuildExplorer();
        SelectPart(map.Objects.Count - 1);
    }

    private void DeleteSelected()
    {
        if (selectedPart >= 0 && selectedPart < map.Objects.Count)
        {
            map.Objects.RemoveAt(selectedPart);
            selectedPart = -1;
        }
        else if (selectedScript >= 0 && selectedScript < map.Scripts.Count)
        {
            map.Scripts.RemoveAt(selectedScript);
            selectedScript = -1;
        }
        RebuildWorkspace();
        RebuildExplorer();
        RefreshProperties();
        UpdateSelectionBox();
    }

    private async void SaveProject(bool publish)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ticket))
            {
                ExportProjectFile();
                status.Text = "Sem ticket do site. Projeto exportado localmente.";
                return;
            }
            map.GameId = await NovusApi.SaveStudioProject(baseUrl, ticket, map, publish);
            status.Text = publish ? $"Publicado no site como jogo {map.GameId}." : $"Rascunho salvo no site como jogo {map.GameId}.";
            Log(status.Text);
        }
        catch (Exception ex)
        {
            status.Text = "Erro ao salvar: " + ex.Message;
            Log(status.Text);
        }
    }

    private void ExportProjectFile()
    {
        var folder = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "NovusWorldsProjects");
        Directory.CreateDirectory(folder);
        var safeName = SafeFileName(string.IsNullOrWhiteSpace(map.Name) ? "NovusPlace" : map.Name);
        var path = Path.Combine(folder, safeName + ".nwm");
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["format"] = "NovusWorldModel",
            ["version"] = 1,
            ["gameId"] = map.GameId,
            ["title"] = map.Name,
            ["description"] = map.Description,
            ["map"] = NovusApi.ToWireMap(map)
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        status.Text = "Exportado: " + path;
        Log(status.Text);
    }

    private void LoadProjectJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var mapJson = root.TryGetProperty("map", out var mapElement) ? mapElement : root;
        var title = root.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? "Novus Place" : "Novus Place";
        map = NovusApi.ParseMap(mapJson, title);
        map.GameId = root.TryGetProperty("gameId", out var idElement) && idElement.TryGetInt32(out var id) ? id : 0;
        map.Description = root.TryGetProperty("description", out var descElement) ? descElement.GetString() ?? "" : "";
        if (map.Scripts.Count == 0) map.Scripts.Add(DefaultScript());
    }

    private void RunLuauPreview()
    {
        Log("Luau preview started.");
        foreach (var script in map.Scripts)
        {
            if (script.Disabled) continue;
            ExecuteLuau(script);
        }
        RebuildWorkspace();
        RefreshProperties();
        Log("Luau preview finished.");
    }

    private void ExecuteLuau(NovusScript script)
    {
        var vars = new Dictionary<string, NovusPart>();
        foreach (var rawLine in (script.Source ?? "").Replace("\r", "").Split('\n'))
        {
            var line = rawLine.Split("--")[0].Trim();
            if (line.Length == 0 || line == "end" || line.StartsWith("if ") || line.StartsWith("game.on")) continue;
            if (line.StartsWith("print(") || line.StartsWith("warn("))
            {
                Log(script.Name + ": " + ExtractCallArgs(line));
                continue;
            }
            var local = line.StartsWith("local ") ? line[6..].Split('=', 2) : Array.Empty<string>();
            if (local.Length == 2 && local[1].Contains("workspace:FindFirstChild"))
            {
                var name = ExtractCallArgs(local[1]).Trim('"', '\'');
                var part = map.Objects.Find(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (part != null) vars[local[0].Trim()] = part;
                continue;
            }
            if (line.Contains(":setColor("))
            {
                var name = line.Split(':')[0].Trim();
                if (vars.TryGetValue(name, out var part)) part.Color = ParseHexColor(ExtractCallArgs(line), part.Color);
            }
            if (line.Contains(":move("))
            {
                var name = line.Split(':')[0].Trim();
                if (vars.TryGetValue(name, out var part)) part.Position += ParseVectorArgs(ExtractCallArgs(line));
            }
            if (line.Contains(":resize("))
            {
                var name = line.Split(':')[0].Trim();
                if (vars.TryGetValue(name, out var part)) part.Size = ParseVectorArgs(ExtractCallArgs(line));
            }
            if (line.Contains(".Anchored"))
            {
                var pieces = line.Split('=', 2);
                var name = pieces[0].Replace(".Anchored", "").Trim();
                if (vars.TryGetValue(name, out var part)) part.Anchored = pieces.Length > 1 && pieces[1].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private void NudgeSelection(Key key, float amount)
    {
        if (selectedPart < 0 || selectedPart >= map.Objects.Count) return;
        var part = map.Objects[selectedPart];
        var delta = Vector3.Zero;
        if (key == Key.Left) delta.X -= amount;
        if (key == Key.Right) delta.X += amount;
        if (key == Key.Up) delta.Z -= amount;
        if (key == Key.Down) delta.Z += amount;
        if (key == Key.Pageup) delta.Y += amount;
        if (key == Key.Pagedown) delta.Y -= amount;
        if (mode == ToolMode.Move) part.Position += delta;
        if (mode == ToolMode.Rotate) part.Rotation += delta * 5f;
        if (mode == ToolMode.Scale) part.Size = new Vector3(Mathf.Max(0.1f, part.Size.X + delta.X), Mathf.Max(0.1f, part.Size.Y + delta.Y), Mathf.Max(0.1f, part.Size.Z + delta.Z));
        RebuildWorkspace();
        RefreshProperties();
    }

    private void FocusSelected()
    {
        if (selectedPart < 0 || selectedPart >= map.Objects.Count) return;
        var target = map.Objects[selectedPart].Position + Vector3.Up * 5f;
        camera.Position = target + new Vector3(14, 10, 14);
        camera.LookAt(target);
    }

    private void SetMode(ToolMode next)
    {
        mode = next;
        status.Text = "Tool: " + mode;
    }

    private static NovusScript DefaultScript() => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = "MainScript",
        Source = "game.on(\"playerJoin\", function(player)\n  player:setHealth(100)\nend)\n\nprint(\"Novus Luau ready\")\n"
    };

    private static Button AddButton(Container parent, string text, Action action)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    private static Label Header(string text) => new() { Text = text, Modulate = new Color(0.05f, 0.12f, 0.2f), HorizontalAlignment = HorizontalAlignment.Left };

    private static Panel Panel(Vector2 position, Vector2 size, Color color)
    {
        var panel = new Panel { Position = position, Size = size };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = color, BorderColor = new Color(0.38f, 0.38f, 0.38f), BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1 });
        return panel;
    }

    private void AddLineEdit(Container parent, string label, string value, Action<string> changed)
    {
        var box = new HBoxContainer();
        box.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(92, 0) });
        var input = new LineEdit { Text = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        input.TextSubmitted += text => { if (!updatingProperties) changed(text); };
        input.FocusExited += () => { if (!updatingProperties) changed(input.Text); };
        box.AddChild(input);
        parent.AddChild(box);
    }

    private void AddVector(Container parent, string label, Vector3 value, Action<Vector3> changed)
    {
        parent.AddChild(new Label { Text = label });
        var row = new HBoxContainer();
        var xs = Spin(value.X, -2048, 2048, 0.1);
        var ys = Spin(value.Y, -2048, 2048, 0.1);
        var zs = Spin(value.Z, -2048, 2048, 0.1);
        void Apply(double _) { if (!updatingProperties) changed(new Vector3((float)xs.Value, (float)ys.Value, (float)zs.Value)); }
        xs.ValueChanged += Apply; ys.ValueChanged += Apply; zs.ValueChanged += Apply;
        row.AddChild(xs); row.AddChild(ys); row.AddChild(zs);
        parent.AddChild(row);
    }

    private void AddFloat(Container parent, string label, float value, double min, double max, Action<float> changed)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(92, 0) });
        var spin = Spin(value, min, max, 0.05);
        spin.ValueChanged += v => { if (!updatingProperties) changed((float)v); };
        row.AddChild(spin);
        parent.AddChild(row);
    }

    private void AddCheck(Container parent, string label, bool value, Action<bool> changed)
    {
        var check = new CheckBox { Text = label, ButtonPressed = value };
        check.Toggled += enabled => { if (!updatingProperties) changed(enabled); };
        parent.AddChild(check);
    }

    private void AddColor(Container parent, string label, Color value, Action<Color> changed)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(92, 0) });
        var picker = new ColorPickerButton { Color = value, CustomMinimumSize = new Vector2(120, 24) };
        picker.ColorChanged += color => { if (!updatingProperties) changed(color); };
        row.AddChild(picker);
        parent.AddChild(row);
    }

    private void AddOption(Container parent, string label, string[] options, string value, Action<string> changed)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(92, 0) });
        var option = new OptionButton();
        for (var i = 0; i < options.Length; i++)
        {
            option.AddItem(options[i]);
            if (options[i].Equals(value, StringComparison.OrdinalIgnoreCase)) option.Select(i);
        }
        option.ItemSelected += index => { if (!updatingProperties) changed(options[(int)index]); };
        row.AddChild(option);
        parent.AddChild(row);
    }

    private static SpinBox Spin(double value, double min, double max, double step)
    {
        return new SpinBox { Value = value, MinValue = min, MaxValue = max, Step = step, CustomMinimumSize = new Vector2(68, 24) };
    }

    private void InsertSnippet(string snippet)
    {
        scriptEditor.InsertTextAtCaret(snippet);
        if (selectedScript >= 0 && selectedScript < map.Scripts.Count) map.Scripts[selectedScript].Source = scriptEditor.Text;
    }

    private void Log(string text)
    {
        output?.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\n");
    }

    private static string IconFor(NovusPart part)
    {
        if (part.Name.Contains("Spawn", StringComparison.OrdinalIgnoreCase)) return "Spawn";
        return part.Type;
    }

    private static bool IsPointerOverUi(Vector2 pos) => pos.X < 220 || pos.X > 1040 || pos.Y < 36 || pos.Y > 555;

    private static string ReadArg(string[] args, string name, string fallback)
    {
        for (var i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return fallback;
    }

    private static string SafeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        return value.Trim();
    }

    private static string ExtractCallArgs(string line)
    {
        var start = line.IndexOf('(');
        var end = line.LastIndexOf(')');
        return start >= 0 && end > start ? line[(start + 1)..end] : "";
    }

    private static Vector3 ParseVectorArgs(string args)
    {
        var parts = args.Split(',', StringSplitOptions.RemoveEmptyEntries);
        float Read(int i) => i < parts.Length && float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0f;
        return new Vector3(Read(0), Read(1), Read(2));
    }

    private static Color ParseHexColor(string value, Color fallback)
    {
        var hex = value.Trim().Trim('"', '\'', '#');
        if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var n))
            return new Color(((n >> 16) & 255) / 255f, ((n >> 8) & 255) / 255f, (n & 255) / 255f);
        return fallback;
    }
}
