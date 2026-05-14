using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

public partial class StudioMain : Node3D
{
    private enum ToolMode { Select, Move, Rotate, Scale, Paint, Measure }

    private NovusMap map = new();
    private Node3D workspace = null!;
    private Camera3D camera = null!;
    private CanvasLayer ui = null!;
    private Control rootUi = null!;
    private Panel toolbarPanel = null!;
    private Panel toolboxPanel = null!;
    private Panel propertiesPanel = null!;
    private Panel scriptPanel = null!;
    private Panel outputPanel = null!;
    private Tree explorer = null!;
    private ScrollContainer propertiesScroll = null!;
    private VBoxContainer properties = null!;
    private TextEdit scriptEditor = null!;
    private RichTextLabel output = null!;
    private Label status = null!;
    private Label titleLabel = null!;
    private Label fpsLabel = null!;
    private Label objectCountLabel = null!;
    private Label mouseWorldLabel = null!;
    private LineEdit explorerSearch = null!;
    private CheckBox snapToggle = null!;
    private OptionButton snapStepSelect = null!;
    private CheckBox wireframeToggle = null!;
    private ColorPickerButton paintPicker = null!;
    private PopupMenu explorerMenu = null!;
    private ConfirmationDialog publishDialog = null!;
    private LineEdit publishName = null!;
    private TextEdit publishDescription = null!;
    private SpinBox publishMaxPlayers = null!;
    private MeshInstance3D? selectionBox;
    private Node3D gizmoRoot = null!;
    private MeshInstance3D gridVisual = null!;
    private FileDialog openDialog = null!;
    private FileDialog importDialog = null!;
    private Panel dashboardPanel = null!;
    private ItemList dashboardGameList = null!;
    private Label dashboardStatus = null!;

    private ToolMode mode = ToolMode.Select;
    private enum GizmoAxis { None, X, Y, Z }
    private int selectedPart = -1;
    private int selectedScript = -1;
    private readonly List<int> selectedParts = new();
    private readonly List<NovusPart> clipboard = new();
    private readonly Stack<string> undo = new();
    private readonly Stack<string> redo = new();
    private readonly List<int> dashboardGameIds = new();
    private readonly Dictionary<int, Vector3> gizmoDragStartPositions = new();
    private readonly Dictionary<int, Vector3> gizmoDragStartSizes = new();
    private readonly Dictionary<int, Vector3> gizmoDragStartRotations = new();
    private string baseUrl = "http://localhost:3000";
    private string ticket = "";
    private bool cameraRotating;
    private bool panningCamera;
    private bool manipulatingSelection;
    private bool updatingProperties;
    private bool dirty;
    private bool gridVisible = true;
    private bool wireframe;
    private bool historyPanelVisible;
    private float snapStep = 1f;
    private float yaw = -45f;
    private float pitch = -32f;
    private double fpsClock;
    private int fpsFrames;
    private Vector2 lastMousePosition;
    private Vector2 gizmoDragStartMouse;
    private Vector3 gizmoCenter;
    private Vector3 gizmoExtents = Vector3.One;
    private Vector3 gizmoAxisLengths = new(3f, 3f, 3f);
    private GizmoAxis activeGizmoAxis = GizmoAxis.None;
    private string gizmoVisualKey = "";

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
        DisplayServer.WindowSetTitle($"Novus Worlds Studio - {map.Name}");
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
            lastMousePosition = mouse.Position;
            if (mouse.ButtonIndex == MouseButton.Right) cameraRotating = mouse.Pressed;
            if (mouse.ButtonIndex == MouseButton.Middle) panningCamera = mouse.Pressed;
            if (mouse.ButtonIndex == MouseButton.Left && !IsPointerOverUi(mouse.Position))
            {
                if (mouse.Pressed)
                {
                    if (mode == ToolMode.Paint) PaintHit(mouse.Position);
                    else if (mode != ToolMode.Select && TryBeginGizmoDrag(mouse.Position))
                    {
                        manipulatingSelection = true;
                    }
                    else
                    {
                        PickPart(mouse.Position, mouse.CtrlPressed);
                        manipulatingSelection = false;
                    }
                }
                else
                {
                    manipulatingSelection = false;
                    activeGizmoAxis = GizmoAxis.None;
                }
            }
            if (mouse.Pressed && mouse.ButtonIndex == MouseButton.WheelUp) camera.Position += -camera.GlobalBasis.Z * 2f;
            if (mouse.Pressed && mouse.ButtonIndex == MouseButton.WheelDown) camera.Position += camera.GlobalBasis.Z * 2f;
        }
        else if (ev is InputEventMouseMotion motion)
        {
            lastMousePosition = motion.Position;
            UpdateMouseWorldLabel(motion.Position);
            if (cameraRotating)
            {
                yaw -= motion.Relative.X * 0.18f;
                pitch = Mathf.Clamp(pitch - motion.Relative.Y * 0.18f, -82f, 82f);
                camera.RotationDegrees = new Vector3(pitch, yaw, 0);
            }
            else if (panningCamera)
            {
                camera.Position += (-camera.GlobalBasis.X * motion.Relative.X + camera.GlobalBasis.Y * motion.Relative.Y) * 0.035f;
            }
            else if (manipulatingSelection)
            {
                ManipulateSelectedWithMouse(motion.Position);
            }
        }
        else if (ev is InputEventKey key && key.Pressed)
        {
            if (scriptEditor?.HasFocus() == true) return;
            if (key.Keycode == Key.Delete) DeleteSelected();
            if (key.Keycode == Key.Escape) ClearSelection();
            if (key.CtrlPressed && key.Keycode == Key.Z) Undo();
            if (key.CtrlPressed && key.Keycode == Key.Y) Redo();
            if (key.CtrlPressed && key.Keycode == Key.D) DuplicateSelected();
            if (key.CtrlPressed && key.Keycode == Key.C) CopySelected();
            if (key.CtrlPressed && key.Keycode == Key.V) PasteClipboard();
            if (key.CtrlPressed && key.Keycode == Key.A) SelectAllParts();
            if (key.CtrlPressed && key.Keycode == Key.G && key.ShiftPressed) UngroupSelected();
            else if (key.CtrlPressed && key.Keycode == Key.G) GroupSelected();
            if (key.CtrlPressed && key.Keycode == Key.S) SaveProject(false);
            if (key.Keycode == Key.F) FocusSelected();
            if (key.Keycode == Key.Key1) SetMode(ToolMode.Select);
            if (key.Keycode == Key.Key2) SetMode(ToolMode.Move);
            if (key.Keycode == Key.Key3) SetMode(ToolMode.Rotate);
            if (key.Keycode == Key.Key4) SetMode(ToolMode.Scale);
            if (key.Keycode == Key.Key5) SetMode(ToolMode.Paint);
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
        if (rootUi != null) LayoutUi();
        UpdateGizmo();
        fpsFrames++;
        fpsClock += delta;
        if (fpsClock >= 0.5)
        {
            if (fpsLabel != null) fpsLabel.Text = $"{Mathf.RoundToInt(fpsFrames / (float)fpsClock)} fps";
            fpsClock = 0;
            fpsFrames = 0;
        }
        if (objectCountLabel != null) objectCountLabel.Text = $"{map.Objects.Count} objetos | {selectedParts.Count} selecionados";
    }

    private void SetupScene()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = map.SkyColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.58f, 0.68f, 0.78f),
                AmbientLightEnergy = 0.85f,
                FogEnabled = false
            }
        });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-45, -35, 0), LightEnergy = 1.8f, ShadowEnabled = true });
        camera = new Camera3D { Name = "StudioCamera", Position = new Vector3(22, 18, 22), RotationDegrees = new Vector3(pitch, yaw, 0), Current = true, Fov = 65f };
        AddChild(camera);
        gridVisual = CreateGrid(128, 1f);
        AddChild(gridVisual);
        gizmoRoot = new Node3D { Name = "TransformGizmo", Visible = false };
        AddChild(gizmoRoot);
        BuildGizmo();
    }

    private void SetupUi()
    {
        ui = new CanvasLayer();
        AddChild(ui);
        rootUi = new Control { Name = "StudioUi" };
        rootUi.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ui.AddChild(rootUi);

        toolbarPanel = Panel(new Vector2(0, 0), new Vector2(1366, 42), new Color(0.09f, 0.15f, 0.23f, 0.98f));
        rootUi.AddChild(toolbarPanel);
        var top = new HBoxContainer { Position = new Vector2(6, 5), Size = new Vector2(1340, 32) };
        toolbarPanel.AddChild(top);
        titleLabel = new Label { Text = $"Novus Worlds Studio - {map.Name}", CustomMinimumSize = new Vector2(235, 24), Modulate = Colors.White };
        top.AddChild(titleLabel);
        AddToolButton(top, "Select", "Selecionar (1)", () => SetMode(ToolMode.Select));
        AddToolButton(top, "Move", "Mover (2)", () => SetMode(ToolMode.Move));
        AddToolButton(top, "Rotate", "Rotacionar (3)", () => SetMode(ToolMode.Rotate));
        AddToolButton(top, "Scale", "Escalar (4)", () => SetMode(ToolMode.Scale));
        AddToolButton(top, "Paint", "Pintar objeto clicado (5)", () => SetMode(ToolMode.Paint));
        snapToggle = new CheckBox { Text = "Snap", ButtonPressed = true, TooltipText = "Snap to Grid" };
        snapToggle.Toggled += on => snapStep = on ? CurrentSnapStep() : 0f;
        top.AddChild(snapToggle);
        snapStepSelect = new OptionButton { CustomMinimumSize = new Vector2(70, 24), TooltipText = "Tamanho da grade" };
        foreach (var label in new[] { "0.25", "0.5", "1", "2", "4" }) snapStepSelect.AddItem(label);
        snapStepSelect.Select(2);
        snapStepSelect.ItemSelected += _ => { snapStep = CurrentSnapStep(); RebuildGrid(); };
        top.AddChild(snapStepSelect);
        wireframeToggle = new CheckBox { Text = "Wire", TooltipText = "Modo wireframe" };
        wireframeToggle.Toggled += value =>
        {
            wireframe = value;
            RenderingServer.ViewportSetDebugDraw(GetViewport().GetViewportRid(), value ? RenderingServer.ViewportDebugDraw.Wireframe : RenderingServer.ViewportDebugDraw.Disabled);
        };
        top.AddChild(wireframeToggle);
        paintPicker = new ColorPickerButton { Color = new Color(0.8f, 0.1f, 0.08f), CustomMinimumSize = new Vector2(38, 24), TooltipText = "Cor da ferramenta Pintar" };
        top.AddChild(paintPicker);
        AddToolButton(top, "Run", "Executar script local", RunLuauPreview);
        AddToolButton(top, "Save", "Salvar (Ctrl+S)", () => SaveProject(false));
        AddToolButton(top, "Publish", "Publicar jogo", OpenPublishDialog);
        AddToolButton(top, "Test Private", "Salvar rascunho e testar no client sem publicar", TestGame);
        AddToolButton(top, "Dashboard", "Abrir painel de projetos", ShowDashboard);
        AddToolButton(top, "Open", "Abrir .nwm", () => openDialog.PopupCentered(new Vector2I(720, 520)));
        AddToolButton(top, "Export", "Exportar .nwm", ExportProjectFile);

        toolboxPanel = Panel(new Vector2(8, 50), new Vector2(224, 515), new Color(0.11f, 0.18f, 0.27f, 0.97f));
        rootUi.AddChild(toolboxPanel);
        var tools = new VBoxContainer { Position = new Vector2(8, 8), Size = new Vector2(208, 500) };
        toolboxPanel.AddChild(tools);
        tools.AddChild(Header("Toolbox"));
        tools.AddChild(Header("Partes Basicas"));
        AddButton(tools, "Cubo", () => AddPart("Part"));
        AddButton(tools, "Esfera", () => AddPart("Ball"));
        AddButton(tools, "Cilindro", () => AddPart("Cylinder"));
        AddButton(tools, "Cunha", () => AddPart("Wedge"));
        AddButton(tools, "CornerWedge", () => AddPart("CornerWedge"));
        AddButton(tools, "Baseplate 512", () => AddBaseplate());
        tools.AddChild(Header("Objetos Especiais"));
        AddButton(tools, "SpawnPoint", AddSpawn);
        AddButton(tools, "PointLight", () => AddLight("PointLight"));
        AddButton(tools, "SurfaceLight", () => AddLight("SurfaceLight"));
        AddButton(tools, "Script", AddScript);
        AddButton(tools, "Model", AddEmptyModel);
        AddButton(tools, "Decal", () => AddPresetPart("Decal", Vector3.Up * 2, new Vector3(4, 0.05f, 4), Colors.White, "Glass"));
        tools.AddChild(Header("Modelos 2008"));
        AddButton(tools, "Casa simples", AddSimpleHouse);
        AddButton(tools, "Arvore", AddTree);
        AddButton(tools, "Pedra", AddStone);
        AddButton(tools, "Obby Starter", AddObbyStarter);
        AddButton(tools, "Classic Tower", AddClassicTower);

        explorerSearch = new LineEdit { PlaceholderText = "Buscar no Explorer", Position = new Vector2(1046, 50), Size = new Vector2(326, 26) };
        explorerSearch.TextChanged += _ => RebuildExplorer();
        rootUi.AddChild(explorerSearch);
        explorer = new Tree { Position = new Vector2(1046, 80), Size = new Vector2(326, 250), HideRoot = false, AllowRmbSelect = true, AllowReselect = true };
        explorer.ItemSelected += OnExplorerSelected;
        explorer.ItemActivated += RenameExplorerItem;
        explorer.ItemEdited += OnExplorerItemEdited;
        explorer.GuiInput += OnExplorerGuiInput;
        rootUi.AddChild(explorer);

        propertiesPanel = Panel(new Vector2(1046, 338), new Vector2(326, 320), new Color(0.12f, 0.19f, 0.28f, 0.98f));
        rootUi.AddChild(propertiesPanel);
        propertiesScroll = new ScrollContainer { Position = new Vector2(8, 8), Size = new Vector2(310, 304) };
        propertiesPanel.AddChild(propertiesScroll);
        properties = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        propertiesScroll.AddChild(properties);

        scriptPanel = Panel(new Vector2(240, 500), new Vector2(796, 170), new Color(0.05f, 0.08f, 0.13f, 0.97f));
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

        outputPanel = Panel(new Vector2(240, 684), new Vector2(796, 76), new Color(0.02f, 0.03f, 0.045f, 0.96f));
        rootUi.AddChild(outputPanel);
        output = new RichTextLabel { Position = new Vector2(8, 6), Size = new Vector2(796, 46), BbcodeEnabled = false, ScrollActive = true, Modulate = new Color(0.75f, 1f, 0.75f) };
        outputPanel.AddChild(output);
        status = new Label { Text = "Novus Studio ready", Position = new Vector2(8, 52), Modulate = Colors.White };
        outputPanel.AddChild(status);
        objectCountLabel = new Label { Text = "0 objetos | 0 selecionados", Position = new Vector2(210, 52), Modulate = Colors.White };
        outputPanel.AddChild(objectCountLabel);
        mouseWorldLabel = new Label { Text = "Mouse: 0, 0, 0", Position = new Vector2(430, 52), Modulate = Colors.White };
        outputPanel.AddChild(mouseWorldLabel);
        fpsLabel = new Label { Text = "0 fps", Position = new Vector2(660, 52), Modulate = new Color(0.8f, 0.95f, 1f) };
        outputPanel.AddChild(fpsLabel);

        openDialog = new FileDialog { Access = FileDialog.AccessEnum.Filesystem, FileMode = FileDialog.FileModeEnum.OpenFile, Filters = new string[] { "*.nwm ; Novus World Model", "*.json ; JSON" } };
        openDialog.FileSelected += path => { LoadProjectJson(File.ReadAllText(path)); RebuildWorkspace(); RebuildExplorer(); ClearSelection(); SelectPart(0); HideDashboard(); Log("Projeto aberto: " + path); };
        rootUi.AddChild(openDialog);

        importDialog = new FileDialog { Access = FileDialog.AccessEnum.Filesystem, FileMode = FileDialog.FileModeEnum.OpenFile, Filters = new string[] { "*.nwm ; Novus World Model", "*.json ; JSON" } };
        rootUi.AddChild(importDialog);
        SetupExplorerMenu();
        SetupPublishDialog();
        SetupDashboard();
        LayoutUi();
        ShowDashboard();
    }

    private void SetupExplorerMenu()
    {
        explorerMenu = new PopupMenu();
        rootUi.AddChild(explorerMenu);
        foreach (var label in new[] { "Duplicate", "Delete", "Group", "Ungroup", "Move Up", "Move Down", "Lock/Unlock", "Hide/Show" })
            explorerMenu.AddItem(label);
        explorerMenu.IdPressed += id =>
        {
            if (id == 0) DuplicateSelected();
            if (id == 1) DeleteSelected();
            if (id == 2) GroupSelected();
            if (id == 3) UngroupSelected();
            if (id == 4) MoveSelectedInList(-1);
            if (id == 5) MoveSelectedInList(1);
            if (id == 6) ToggleLockSelected();
            if (id == 7) ToggleVisibleSelected();
        };
    }

    private void SetupPublishDialog()
    {
        publishDialog = new ConfirmationDialog { Title = "Publicar Jogo", Size = new Vector2I(520, 420), OkButtonText = "Publicar" };
        var box = new VBoxContainer();
        publishDialog.AddChild(box);
        box.AddChild(new Label { Text = "Nome do jogo" });
        publishName = new LineEdit { Text = map.Name };
        box.AddChild(publishName);
        box.AddChild(new Label { Text = "Descricao" });
        publishDescription = new TextEdit { Text = map.Description, CustomMinimumSize = new Vector2(460, 120) };
        box.AddChild(publishDescription);
        box.AddChild(new Label { Text = "Thumbnail sera capturada do viewport ao salvar." });
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Max players", CustomMinimumSize = new Vector2(120, 0) });
        publishMaxPlayers = new SpinBox { MinValue = 1, MaxValue = 20, Value = 20 };
        row.AddChild(publishMaxPlayers);
        box.AddChild(row);
        publishDialog.Confirmed += () =>
        {
            map.Name = publishName.Text.Trim().Length == 0 ? map.Name : publishName.Text.Trim();
            map.Description = publishDescription.Text;
            map.MaxPlayers = Mathf.Clamp((int)publishMaxPlayers.Value, 1, 20);
            SaveProject(true);
        };
        rootUi.AddChild(publishDialog);
    }

    private void SetupDashboard()
    {
        dashboardPanel = Panel(new Vector2(260, 86), new Vector2(820, 560), new Color(0.06f, 0.11f, 0.18f, 0.98f));
        dashboardPanel.Name = "StudioDashboard";
        rootUi.AddChild(dashboardPanel);

        dashboardPanel.AddChild(new Label
        {
            Text = "Novus Worlds Studio",
            Position = new Vector2(24, 18),
            Size = new Vector2(500, 34),
            Modulate = Colors.White
        });
        dashboardPanel.AddChild(new Label
        {
            Text = "Escolha um projeto, crie um novo mundo ou importe um arquivo .nwm.",
            Position = new Vector2(24, 54),
            Size = new Vector2(720, 24),
            Modulate = new Color(0.82f, 0.91f, 1f)
        });

        dashboardGameList = new ItemList
        {
            Position = new Vector2(24, 96),
            Size = new Vector2(520, 380),
            AllowReselect = true,
            CustomMinimumSize = new Vector2(520, 380)
        };
        dashboardGameList.ItemActivated += _ => OpenDashboardSelectedGame();
        dashboardPanel.AddChild(dashboardGameList);

        var actions = new VBoxContainer { Position = new Vector2(574, 96), Size = new Vector2(220, 300) };
        dashboardPanel.AddChild(actions);
        AddButton(actions, "Continuar projeto atual", HideDashboard);
        AddButton(actions, "Criar jogo novo", NewProjectFromDashboard);
        AddButton(actions, "Template NVX Storm Island", LoadStormIslandTemplate);
        AddButton(actions, "Abrir selecionado", OpenDashboardSelectedGame);
        AddButton(actions, "Importar .nwm", () => openDialog.PopupCentered(new Vector2I(720, 520)));
        AddButton(actions, "Salvar atual", () => SaveProject(false));
        AddButton(actions, "Publicar atual", OpenPublishDialog);

        dashboardStatus = new Label
        {
            Text = "Carregando suas criacoes...",
            Position = new Vector2(24, 492),
            Size = new Vector2(760, 28),
            Modulate = new Color(0.75f, 1f, 0.75f)
        };
        dashboardPanel.AddChild(dashboardStatus);
        _ = LoadDashboardGames();
    }

    private void ShowDashboard()
    {
        if (dashboardPanel == null) return;
        dashboardPanel.Visible = true;
        dashboardPanel.MoveToFront();
        _ = LoadDashboardGames();
    }

    private void HideDashboard()
    {
        if (dashboardPanel != null) dashboardPanel.Visible = false;
    }

    private async Task LoadDashboardGames()
    {
        if (dashboardGameList == null) return;
        dashboardGameList.Clear();
        dashboardGameIds.Clear();
        if (string.IsNullOrWhiteSpace(ticket))
        {
            dashboardStatus.Text = "Sem ticket do site. Use Importar .nwm ou Criar jogo novo local.";
            dashboardGameList.AddItem("Projeto local atual");
            dashboardGameIds.Add(map.GameId);
            return;
        }
        try
        {
            var games = await NovusApi.LoadStudioGames(baseUrl, ticket);
            if (games.Count == 0)
            {
                dashboardStatus.Text = "Voce ainda nao tem criacoes salvas. Clique em Criar jogo novo.";
                return;
            }
            foreach (var game in games)
            {
                dashboardGameList.AddItem($"{game.Title}  #{game.Id}  {(game.IsActive ? "Publicado" : "Rascunho")}");
                dashboardGameIds.Add(game.Id);
            }
            dashboardStatus.Text = $"{games.Count} criacao(oes) encontradas.";
        }
        catch (Exception ex)
        {
            dashboardStatus.Text = "Nao consegui carregar suas criacoes: " + ex.Message;
        }
    }

    private async void OpenDashboardSelectedGame()
    {
        if (dashboardGameList == null || dashboardGameList.GetSelectedItems().Length == 0)
        {
            dashboardStatus.Text = "Selecione um projeto primeiro.";
            return;
        }
        var selected = dashboardGameList.GetSelectedItems()[0];
        if (selected < 0 || selected >= dashboardGameIds.Count || dashboardGameIds[selected] <= 0)
        {
            HideDashboard();
            return;
        }
        try
        {
            dashboardStatus.Text = "Abrindo projeto...";
            map = await NovusApi.LoadStudioProject(baseUrl, ticket, dashboardGameIds[selected]);
            if (map.Scripts.Count == 0) map.Scripts.Add(DefaultScript());
            RebuildWorkspace();
            RebuildExplorer();
            ClearSelection();
            SelectPart(0);
            dirty = false;
            UpdateWindowTitle();
            HideDashboard();
            Log("Projeto aberto: " + map.Name);
        }
        catch (Exception ex)
        {
            dashboardStatus.Text = "Erro ao abrir projeto: " + ex.Message;
        }
    }

    private void NewProjectFromDashboard()
    {
        PushUndo();
        map = CreateBlankProject();
        RebuildWorkspace();
        RebuildExplorer();
        ClearSelection();
        SelectPart(0);
        dirty = true;
        UpdateWindowTitle();
        HideDashboard();
        Log("Novo projeto criado.");
    }

    private void LoadStormIslandTemplate()
    {
        PushUndo();
        map = NovusTemplates.StormIsland();
        RebuildWorkspace();
        RebuildExplorer();
        ClearSelection();
        SelectPart(0);
        dirty = true;
        UpdateWindowTitle();
        HideDashboard();
        Log("Template NVX Storm Island carregado.");
    }

    private void OpenPublishDialog()
    {
        publishName.Text = map.Name;
        publishDescription.Text = map.Description;
        publishMaxPlayers.Value = map.MaxPlayers;
        publishDialog.PopupCentered();
    }

    private void OnExplorerGuiInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Right)
        {
            explorerMenu.Position = DisplayServer.MouseGetPosition();
            explorerMenu.Popup();
        }
    }

    private void RenameExplorerItem()
    {
        var item = explorer.GetSelected();
        if (item == null) return;
        item.SetEditable(0, true);
        explorer.EditSelected(true);
    }

    private void OnExplorerItemEdited()
    {
        var item = explorer.GetEdited();
        var meta = item?.GetMetadata(0).AsString() ?? "";
        if (meta.StartsWith("part:"))
        {
            var id = meta[5..];
            var part = map.Objects.Find(p => p.Id == id);
            if (part != null) part.Name = item?.GetText(0).ToString().Replace(IconFor(part), "").Trim() ?? part.Name;
        }
        if (meta.StartsWith("script:"))
        {
            var id = meta[7..];
            var script = map.Scripts.Find(s => s.Id == id);
            if (script != null) script.Name = item?.GetText(0).ToString().Replace("Script", "").Trim() ?? script.Name;
        }
        MarkDirty("Renamed");
        RebuildExplorer();
        RefreshProperties();
    }

    private void MoveSelectedInList(int dir)
    {
        if (selectedPart < 0 || selectedPart >= map.Objects.Count) return;
        var target = selectedPart + dir;
        if (target < 0 || target >= map.Objects.Count) return;
        PushUndo();
        (map.Objects[selectedPart], map.Objects[target]) = (map.Objects[target], map.Objects[selectedPart]);
        selectedPart = target;
        selectedParts.Clear();
        selectedParts.Add(target);
        MarkDirty("Explorer order changed");
        RebuildWorkspace();
        RebuildExplorer();
        UpdateSelectionBox();
    }

    private void ToggleLockSelected()
    {
        if (selectedPart < 0 || selectedPart >= map.Objects.Count) return;
        PushUndo();
        foreach (var index in selectedParts.Count > 0 ? selectedParts : new List<int> { selectedPart })
            if (index >= 0 && index < map.Objects.Count) map.Objects[index].Locked = !map.Objects[index].Locked;
        MarkDirty("Lock toggled");
        RebuildExplorer();
        RefreshProperties();
    }

    private void ToggleVisibleSelected()
    {
        if (selectedPart < 0 || selectedPart >= map.Objects.Count) return;
        PushUndo();
        foreach (var index in selectedParts.Count > 0 ? selectedParts : new List<int> { selectedPart })
            if (index >= 0 && index < map.Objects.Count) map.Objects[index].Visible = !map.Objects[index].Visible;
        MarkDirty("Visibility toggled");
        RebuildWorkspace();
        RebuildExplorer();
        RefreshProperties();
    }

    private void LayoutUi()
    {
        var size = GetViewport().GetVisibleRect().Size;
        rootUi.Size = size;
        toolbarPanel.Position = Vector2.Zero;
        toolbarPanel.Size = new Vector2(size.X, 42);
        toolboxPanel.Position = new Vector2(8, 50);
        toolboxPanel.Size = new Vector2(224, Mathf.Max(360, size.Y - 146));
        var rightPanelX = Mathf.Max(800, size.X - 342);
        explorerSearch.Position = new Vector2(rightPanelX, 50);
        explorerSearch.Size = new Vector2(334, 26);
        explorer.Position = new Vector2(rightPanelX, 80);
        explorer.Size = new Vector2(334, Mathf.Max(190, (size.Y - 172) * 0.44f));
        propertiesPanel.Position = new Vector2(rightPanelX, explorer.Position.Y + explorer.Size.Y + 8);
        propertiesPanel.Size = new Vector2(334, Mathf.Max(250, size.Y - propertiesPanel.Position.Y - 86));
        propertiesScroll.Size = new Vector2(propertiesPanel.Size.X - 16, propertiesPanel.Size.Y - 16);
        properties.CustomMinimumSize = new Vector2(propertiesScroll.Size.X - 18, 0);

        var centerX = toolboxPanel.Position.X + toolboxPanel.Size.X + 8;
        var rightX = rightPanelX - 8;
        var centerW = Mathf.Max(520, rightX - centerX);
        scriptPanel.Position = new Vector2(centerX, Mathf.Max(400, size.Y - 248));
        scriptPanel.Size = new Vector2(centerW, 160);
        scriptEditor.Position = new Vector2(8, 28);
        scriptEditor.Size = new Vector2(centerW - 16, 122);
        outputPanel.Position = new Vector2(centerX, size.Y - 82);
        outputPanel.Size = new Vector2(centerW, 74);
        output.Size = new Vector2(centerW - 16, 42);
        status.Position = new Vector2(8, 48);
        objectCountLabel.Position = new Vector2(Mathf.Min(240, centerW * 0.26f), 48);
        mouseWorldLabel.Position = new Vector2(Mathf.Min(460, centerW * 0.52f), 48);
        fpsLabel.Position = new Vector2(Mathf.Max(650, centerW - 76), 48);
        if (dashboardPanel != null)
        {
            dashboardPanel.Size = new Vector2(Mathf.Min(860, size.X - 80), Mathf.Min(580, size.Y - 90));
            dashboardPanel.Position = new Vector2((size.X - dashboardPanel.Size.X) * 0.5f, 62);
        }
    }

    private float CurrentSnapStep()
    {
        if (snapStepSelect == null) return 1f;
        return snapStepSelect.Selected switch { 0 => 0.25f, 1 => 0.5f, 2 => 1f, 3 => 2f, _ => 4f };
    }

    private Vector3 Snap(Vector3 value)
    {
        var step = snapToggle != null && snapToggle.ButtonPressed ? CurrentSnapStep() : 0f;
        if (step <= 0) return value;
        return new Vector3(Mathf.Round(value.X / step) * step, Mathf.Round(value.Y / step) * step, Mathf.Round(value.Z / step) * step);
    }

    private void RebuildGrid()
    {
        gridVisual?.QueueFree();
        gridVisual = CreateGrid(128, CurrentSnapStep());
        gridVisual.Visible = gridVisible;
        AddChild(gridVisual);
    }

    private MeshInstance3D CreateGrid(int studs, float step)
    {
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        var half = studs / 2f;
        var color = new Color(0.38f, 0.58f, 0.75f, 0.45f);
        for (var i = -half; i <= half; i += Mathf.Max(step, 0.25f))
        {
            mesh.SurfaceSetColor(color);
            mesh.SurfaceAddVertex(new Vector3(i, 0.02f, -half));
            mesh.SurfaceAddVertex(new Vector3(i, 0.02f, half));
            mesh.SurfaceAddVertex(new Vector3(-half, 0.02f, i));
            mesh.SurfaceAddVertex(new Vector3(half, 0.02f, i));
        }
        mesh.SurfaceEnd();
        return new MeshInstance3D
        {
            Name = "StudioGrid",
            Mesh = mesh,
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = Colors.White, Transparency = BaseMaterial3D.TransparencyEnum.Alpha }
        };
    }

    private void BuildGizmo()
    {
        foreach (var child in gizmoRoot.GetChildren())
        {
            gizmoRoot.RemoveChild(child);
            child.QueueFree();
        }
        var lx = gizmoAxisLengths.X;
        var ly = gizmoAxisLengths.Y;
        var lz = gizmoAxisLengths.Z;
        gizmoRoot.AddChild(GizmoArrow("X", Colors.Red, new Vector3(lx, 0, 0), new Vector3(0, 0, -90)));
        gizmoRoot.AddChild(GizmoArrow("Y", Colors.Green, new Vector3(0, ly, 0), Vector3.Zero));
        gizmoRoot.AddChild(GizmoArrow("Z", Colors.Blue, new Vector3(0, 0, lz), new Vector3(90, 0, 0)));
        gizmoRoot.AddChild(GizmoCube("ScaleX", Colors.Red, new Vector3(lx, 0, 0)));
        gizmoRoot.AddChild(GizmoCube("ScaleY", Colors.Green, new Vector3(0, ly, 0)));
        gizmoRoot.AddChild(GizmoCube("ScaleZ", Colors.Blue, new Vector3(0, 0, lz)));
    }

    private Node3D GizmoArrow(string name, Color color, Vector3 position, Vector3 rotation)
    {
        var root = new Node3D { Name = name };
        var mat = new StandardMaterial3D { AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        var length = Mathf.Max(0.1f, position.Length());
        var shaft = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.055f, BottomRadius = 0.055f, Height = length }, Position = position * 0.5f, RotationDegrees = rotation, MaterialOverride = mat };
        var head = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.22f, Height = 0.55f }, Position = position, RotationDegrees = rotation, MaterialOverride = mat };
        root.AddChild(shaft);
        root.AddChild(head);
        return root;
    }

    private Node3D GizmoCube(string name, Color color, Vector3 position)
    {
        return new MeshInstance3D { Name = name, Mesh = new BoxMesh { Size = Vector3.One * 0.26f }, Position = position, MaterialOverride = new StandardMaterial3D { AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } };
    }

    private void UpdateGizmo()
    {
        if (gizmoRoot == null || selectedPart < 0 || selectedPart >= map.Objects.Count)
        {
            if (gizmoRoot != null) gizmoRoot.Visible = false;
            return;
        }
        var bounds = SelectionBounds();
        gizmoCenter = bounds.Position + bounds.Size * 0.5f;
        gizmoExtents = new Vector3(Mathf.Max(0.5f, bounds.Size.X * 0.5f), Mathf.Max(0.5f, bounds.Size.Y * 0.5f), Mathf.Max(0.5f, bounds.Size.Z * 0.5f));
        gizmoAxisLengths = new Vector3(gizmoExtents.X + 2.2f, gizmoExtents.Y + 2.2f, gizmoExtents.Z + 2.2f);
        var visualKey = $"{mode}:{gizmoAxisLengths.X:0.00}:{gizmoAxisLengths.Y:0.00}:{gizmoAxisLengths.Z:0.00}";
        if (visualKey != gizmoVisualKey)
        {
            gizmoVisualKey = visualKey;
            BuildGizmo();
        }
        gizmoRoot.Position = gizmoCenter;
        gizmoRoot.Visible = selectedPart >= 0 && mode != ToolMode.Select && mode != ToolMode.Paint;
        foreach (var child in gizmoRoot.GetChildren())
            if (child is Node3D node)
                node.Visible = mode == ToolMode.Scale ? node.Name.ToString().StartsWith("Scale") : !node.Name.ToString().StartsWith("Scale");
    }

    private void ManipulateSelectedWithMouse(Vector2 mousePosition)
    {
        if (selectedPart < 0 || selectedPart >= map.Objects.Count || activeGizmoAxis == GizmoAxis.None) return;
        var axis = AxisVector(activeGizmoAxis);
        var projected = ProjectAxisToScreen(axis);
        if (projected.LengthSquared() < 0.0001f) return;
        var screenDelta = mousePosition - gizmoDragStartMouse;
        var pixels = screenDelta.Dot(projected.Normalized());
        var worldScale = Mathf.Max(0.02f, 4f / Mathf.Max(36f, projected.Length()));
        var amount = pixels * worldScale;
        var rotationAmount = pixels * 0.45f;
        foreach (var index in selectedParts.Count > 0 ? selectedParts : new List<int> { selectedPart })
        {
            if (index < 0 || index >= map.Objects.Count || map.Objects[index].Locked) continue;
            var part = map.Objects[index];
            if (mode == ToolMode.Move && gizmoDragStartPositions.TryGetValue(index, out var startPosition))
                part.Position = Snap(startPosition + axis * amount);
            if (mode == ToolMode.Rotate && gizmoDragStartRotations.TryGetValue(index, out var startRotation))
                part.Rotation = startRotation + axis * rotationAmount;
            if (mode == ToolMode.Scale)
            {
                var startSize = gizmoDragStartSizes.TryGetValue(index, out var savedSize) ? savedSize : part.Size;
                part.Size = new Vector3(
                    activeGizmoAxis == GizmoAxis.X ? Mathf.Max(0.1f, startSize.X + amount) : startSize.X,
                    activeGizmoAxis == GizmoAxis.Y ? Mathf.Max(0.1f, startSize.Y + amount) : startSize.Y,
                    activeGizmoAxis == GizmoAxis.Z ? Mathf.Max(0.1f, startSize.Z + amount) : startSize.Z
                );
            }
        }
        dirty = true;
        RebuildWorkspace();
        UpdateSelectionBox();
        RefreshProperties();
    }

    private void PaintHit(Vector2 screenPosition)
    {
        var from = camera.ProjectRayOrigin(screenPosition);
        var to = from + camera.ProjectRayNormal(screenPosition) * 2000f;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to));
        if (hit.Count == 0 || !hit.TryGetValue("collider", out var colliderValue) || colliderValue.AsGodotObject() is not Node node) return;
        var id = FindNovusId(node);
        var index = map.Objects.FindIndex(part => part.Id == id);
        if (index < 0 || map.Objects[index].Locked) return;
        PushUndo();
        map.Objects[index].Color = paintPicker.Color;
        MarkDirty("Painted");
        RebuildWorkspace();
        SelectPart(index);
    }

    private void UpdateMouseWorldLabel(Vector2 screenPosition)
    {
        if (mouseWorldLabel == null) return;
        var from = camera.ProjectRayOrigin(screenPosition);
        var dir = camera.ProjectRayNormal(screenPosition);
        if (Mathf.Abs(dir.Y) < 0.001f) return;
        var t = -from.Y / dir.Y;
        var point = from + dir * t;
        mouseWorldLabel.Text = $"Mouse: {point.X:0.0}, {point.Y:0.0}, {point.Z:0.0}";
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
        var q = explorerSearch?.Text?.Trim() ?? "";
        foreach (var part in map.Objects)
            if (string.IsNullOrWhiteSpace(part.ParentId))
                AddPartToExplorer(workspaceItem, part, q);
        var scriptsItem = explorer.CreateItem(root);
        scriptsItem.SetText(0, "ServerScriptService");
        scriptsItem.SetMetadata(0, "scripts");
        foreach (var script in map.Scripts)
        {
            if (!string.IsNullOrWhiteSpace(q) && !script.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            var item = explorer.CreateItem(scriptsItem);
            item.SetText(0, "Script " + script.Name);
            item.SetMetadata(0, "script:" + script.Id);
        }
        explorer.GetRoot()?.SetCollapsed(false);
    }

    private void AddPartToExplorer(TreeItem parent, NovusPart part, string filter)
    {
        var include = string.IsNullOrWhiteSpace(filter)
            || part.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || map.Objects.Exists(child => child.ParentId == part.Id && child.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
        if (!include) return;
        var item = explorer.CreateItem(parent);
        item.SetText(0, $"{IconFor(part)} {(part.Visible ? "" : "[Hidden] ")}{(part.Locked ? "[Locked] " : "")}{part.Name}");
        item.SetMetadata(0, "part:" + part.Id);
        foreach (var child in map.Objects)
            if (child.ParentId == part.Id)
                AddPartToExplorer(item, child, filter);
        foreach (var script in map.Scripts)
            if (script.ParentId == part.Id)
            {
                var scriptItem = explorer.CreateItem(item);
                scriptItem.SetText(0, "Script " + script.Name);
                scriptItem.SetMetadata(0, "script:" + script.Id);
            }
    }

    private void RefreshProperties()
    {
        updatingProperties = true;
        foreach (var child in properties.GetChildren()) child.QueueFree();
        if (selectedPart >= 0 && selectedPart < map.Objects.Count)
        {
            var part = map.Objects[selectedPart];
            properties.AddChild(Header(selectedParts.Count > 1 ? $"Properties ({selectedParts.Count})" : "Properties"));
            AddTransformButtons(properties);
            AddLineEdit(properties, "Name", part.Name, value => { part.Name = value; RebuildWorkspace(); RebuildExplorer(); });
            AddOption(properties, "Type", new[] { "Part", "Ball", "Sphere", "Cylinder", "Wedge", "CornerWedge", "SpawnPoint", "PointLight", "SurfaceLight", "Model", "Decal" }, part.Type, value => { part.Type = value; MarkDirty("Type changed"); RebuildWorkspace(); RebuildExplorer(); });
            AddVector(properties, "Position", part.Position, value => { part.Position = value; RebuildWorkspace(); UpdateSelectionBox(); });
            AddVector(properties, "Rotation", part.Rotation, value => { part.Rotation = value; RebuildWorkspace(); UpdateSelectionBox(); });
            AddVector(properties, "Size", part.Size, value => { part.Size = new Vector3(Mathf.Max(0.1f, value.X), Mathf.Max(0.1f, value.Y), Mathf.Max(0.1f, value.Z)); RebuildWorkspace(); UpdateSelectionBox(); });
            AddColor(properties, "Color", part.Color, value => { part.Color = value; RebuildWorkspace(); });
            AddLineEdit(properties, "Hex", ColorToHex(part.Color), value => { part.Color = ParseHexColor(value, part.Color); RebuildWorkspace(); });
            AddPalette(properties, part);
            AddOption(properties, "Material", new[] { "Plastic", "Metal", "Wood", "Stone", "Grass", "Brick", "Glass", "Neon", "Ice" }, part.Material, value => { part.Material = value; RebuildWorkspace(); });
            AddCheck(properties, "Anchored", part.Anchored, value => { part.Anchored = value; RebuildWorkspace(); });
            AddCheck(properties, "CanCollide", part.CanCollide, value => { part.CanCollide = value; RebuildWorkspace(); });
            AddCheck(properties, "Locked", part.Locked, value => { part.Locked = value; });
            AddCheck(properties, "Visible", part.Visible, value => { part.Visible = value; RebuildWorkspace(); RebuildExplorer(); });
            AddCheck(properties, "CastShadow", part.CastShadow, value => { part.CastShadow = value; RebuildWorkspace(); });
            AddFloat(properties, "Transparency", part.Transparency, 0, 1, value => { part.Transparency = value; RebuildWorkspace(); });
            AddFloat(properties, "Reflectance", part.Reflectance, 0, 1, value => { part.Reflectance = value; RebuildWorkspace(); });
            if (part.Type == "PointLight" || part.Type == "SurfaceLight")
            {
                AddFloat(properties, "Brightness", part.Brightness, 0, 8, value => { part.Brightness = value; RebuildWorkspace(); });
                AddFloat(properties, "Range", part.Range, 1, 128, value => { part.Range = value; RebuildWorkspace(); });
            }
            AddButton(properties, "Copy Properties", CopyPropertiesFromSelected);
            AddButton(properties, "Paste Properties", PastePropertiesToSelected);
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

    private void PickPart(Vector2 screenPosition, bool append = false)
    {
        var from = camera.ProjectRayOrigin(screenPosition);
        var to = from + camera.ProjectRayNormal(screenPosition) * 2000f;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to));
        if (hit.Count == 0) { if (!append) ClearSelection(); return; }
        if (!hit.TryGetValue("collider", out var colliderValue)) return;
        if (colliderValue.AsGodotObject() is not Node node) return;
        var id = FindNovusId(node);
        if (string.IsNullOrWhiteSpace(id)) return;
        SelectPart(map.Objects.FindIndex(part => part.Id == id), append);
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

    private void SelectPart(int index, bool append = false)
    {
        selectedPart = index;
        selectedScript = -1;
        scriptEditor.Text = "";
        if (!append) selectedParts.Clear();
        if (index >= 0 && !selectedParts.Contains(index)) selectedParts.Add(index);
        if (append && selectedParts.Count > 0) selectedPart = selectedParts[^1];
        if (selectedPart >= 0 && selectedPart < map.Objects.Count) status.Text = "Selected: " + map.Objects[selectedPart].Name;
        RefreshProperties();
        UpdateSelectionBox();
    }

    private void SelectScript(int index)
    {
        selectedScript = index;
        selectedPart = -1;
        selectedParts.Clear();
        if (selectedScript >= 0 && selectedScript < map.Scripts.Count)
        {
            scriptEditor.Text = map.Scripts[selectedScript].Source;
            status.Text = "Editing script: " + map.Scripts[selectedScript].Name;
        }
        RefreshProperties();
        UpdateSelectionBox();
    }

    private void ClearSelection()
    {
        selectedPart = -1;
        selectedScript = -1;
        selectedParts.Clear();
        scriptEditor.Text = "";
        RefreshProperties();
        UpdateSelectionBox();
    }

    private void UpdateSelectionBox()
    {
        selectionBox?.QueueFree();
        selectionBox = null;
        gizmoRoot.Visible = false;
        if (selectedPart < 0 || selectedPart >= map.Objects.Count) return;
        var bounds = SelectionBounds();
        selectionBox = new MeshInstance3D
        {
            Name = "SelectionBox",
            Position = bounds.Position + bounds.Size * 0.5f,
            Mesh = new BoxMesh { Size = bounds.Size + new Vector3(0.12f, 0.12f, 0.12f) },
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
        gizmoRoot.Visible = true;
        UpdateGizmo();
    }

    private List<int> ActiveSelectionIndices()
    {
        if (selectedParts.Count > 0) return new List<int>(selectedParts);
        return selectedPart >= 0 ? new List<int> { selectedPart } : new List<int>();
    }

    private Aabb SelectionBounds()
    {
        var indices = ActiveSelectionIndices();
        var hasBounds = false;
        var bounds = new Aabb();
        foreach (var index in indices)
        {
            if (index < 0 || index >= map.Objects.Count) continue;
            var part = map.Objects[index];
            var half = new Vector3(Mathf.Max(0.2f, part.Size.X), Mathf.Max(0.2f, part.Size.Y), Mathf.Max(0.2f, part.Size.Z)) * 0.5f;
            var partBounds = new Aabb(part.Position - half, half * 2f);
            if (!hasBounds)
            {
                bounds = partBounds;
                hasBounds = true;
            }
            else
            {
                bounds = bounds.Expand(partBounds.Position);
                bounds = bounds.Expand(partBounds.Position + partBounds.Size);
            }
        }
        return hasBounds ? bounds : new Aabb(Vector3.Zero, Vector3.One);
    }

    private bool TryBeginGizmoDrag(Vector2 screenPosition)
    {
        UpdateGizmo();
        if (!gizmoRoot.Visible) return false;
        var axis = PickGizmoAxis(screenPosition);
        if (axis == GizmoAxis.None) return false;
        PushUndo();
        activeGizmoAxis = axis;
        gizmoDragStartMouse = screenPosition;
        gizmoDragStartPositions.Clear();
        gizmoDragStartSizes.Clear();
        gizmoDragStartRotations.Clear();
        foreach (var index in ActiveSelectionIndices())
        {
            if (index < 0 || index >= map.Objects.Count) continue;
            gizmoDragStartPositions[index] = map.Objects[index].Position;
            gizmoDragStartSizes[index] = map.Objects[index].Size;
            gizmoDragStartRotations[index] = map.Objects[index].Rotation;
        }
        status.Text = $"{mode} eixo {axis}";
        return true;
    }

    private GizmoAxis PickGizmoAxis(Vector2 screenPosition)
    {
        var bestAxis = GizmoAxis.None;
        var bestDistance = 9999f;
        foreach (var axis in new[] { GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z })
        {
            var distance = DistanceToProjectedAxis(screenPosition, axis);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestAxis = axis;
            }
        }
        return bestDistance <= 24f ? bestAxis : GizmoAxis.None;
    }

    private float DistanceToProjectedAxis(Vector2 point, GizmoAxis axis)
    {
        var start = camera.UnprojectPosition(gizmoCenter);
        var end = camera.UnprojectPosition(gizmoCenter + AxisVector(axis) * gizmoAxisLengths[(int)axis - 1]);
        var segment = end - start;
        var lengthSq = segment.LengthSquared();
        if (lengthSq < 0.001f) return 9999f;
        var t = Mathf.Clamp((point - start).Dot(segment) / lengthSq, 0f, 1f);
        return point.DistanceTo(start + segment * t);
    }

    private Vector2 ProjectAxisToScreen(Vector3 axis)
    {
        var start = camera.UnprojectPosition(gizmoCenter);
        var end = camera.UnprojectPosition(gizmoCenter + axis * 4f);
        return end - start;
    }

    private static Vector3 AxisVector(GizmoAxis axis)
    {
        return axis switch
        {
            GizmoAxis.X => Vector3.Right,
            GizmoAxis.Y => Vector3.Up,
            GizmoAxis.Z => Vector3.Back,
            _ => Vector3.Zero
        };
    }

    private static NovusMap CreateBlankProject()
    {
        var next = new NovusMap
        {
            Name = "Novo Mundo",
            Description = "Criado no Novus Worlds Studio.",
            SkyColor = new Color(0.53f, 0.81f, 0.92f),
            Spawn = new Vector3(0, 4, 0),
            MaxPlayers = 20
        };
        next.Objects.Add(new NovusPart
        {
            Id = "baseplate",
            Type = "Part",
            Name = "Baseplate",
            Position = new Vector3(0, -0.5f, 0),
            Size = new Vector3(128, 1, 128),
            Color = new Color(0.34f, 0.66f, 0.32f),
            Material = "Grass",
            Anchored = true,
            CanCollide = true
        });
        next.Objects.Add(new NovusPart
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = "SpawnPoint",
            Name = "SpawnPoint",
            Position = new Vector3(0, 0.25f, 0),
            Size = new Vector3(6, 0.5f, 6),
            Color = new Color(0.1f, 0.9f, 0.25f),
            Material = "Neon",
            Anchored = true,
            CanCollide = true
        });
        next.Scripts.Add(DefaultScript());
        return next;
    }

    private void AddPart(string type)
    {
        PushUndo();
        var part = new NovusPart
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = type,
            Name = type,
            Position = camera.Position + -camera.GlobalBasis.Z * 10f,
            Size = type == "Ball" || type == "Sphere" ? new Vector3(4, 4, 4) : type == "Cylinder" ? new Vector3(4, 4, 4) : type == "SpawnPoint" ? new Vector3(6, 0.5f, 6) : new Vector3(4, 1, 4),
            Color = Colors.LightGray,
            Material = type == "SpawnPoint" ? "Neon" : "Plastic",
            CanCollide = type != "PointLight" && type != "SurfaceLight" && type != "Model"
        };
        map.Objects.Add(part);
        MarkDirty($"{type} added");
        RebuildWorkspace();
        RebuildExplorer();
        SelectPart(map.Objects.Count - 1);
    }

    private void AddPresetPart(string name, Vector3 position, Vector3 size, Color color, string material)
    {
        PushUndo();
        map.Objects.Add(new NovusPart { Id = Guid.NewGuid().ToString("N"), Name = name, Position = position, Size = size, Color = color, Material = material, Anchored = true, CanCollide = true });
        MarkDirty($"{name} added");
        RebuildWorkspace();
        RebuildExplorer();
        SelectPart(map.Objects.Count - 1);
    }

    private void AddSpawn()
    {
        PushUndo();
        map.Spawn = new Vector3(0, 4, 0);
        map.Objects.Add(new NovusPart { Id = Guid.NewGuid().ToString("N"), Type = "SpawnPoint", Name = "SpawnPoint", Position = new Vector3(0, 0.25f, 0), Size = new Vector3(6, 0.5f, 6), Color = new Color(0.1f, 0.9f, 0.25f), Material = "Neon", Anchored = true, CanCollide = true });
        MarkDirty("Spawn added");
        RebuildWorkspace();
        RebuildExplorer();
        SelectPart(map.Objects.Count - 1);
    }

    private void AddBaseplate()
    {
        PushUndo();
        map.Objects.Add(new NovusPart { Id = Guid.NewGuid().ToString("N"), Type = "Part", Name = "Baseplate", Position = new Vector3(0, -0.5f, 0), Size = new Vector3(512, 1, 512), Color = new Color(0.34f, 0.66f, 0.32f), Material = "Grass", Anchored = true, CanCollide = true });
        MarkDirty("Baseplate added");
        RebuildWorkspace();
        RebuildExplorer();
        SelectPart(map.Objects.Count - 1);
    }

    private void AddLight(string type)
    {
        PushUndo();
        map.Objects.Add(new NovusPart { Id = Guid.NewGuid().ToString("N"), Type = type, Name = type, Position = camera.Position + -camera.GlobalBasis.Z * 8f, Size = Vector3.One, Color = Colors.White, Material = "Neon", Anchored = true, CanCollide = false, Brightness = 2f, Range = 24f });
        MarkDirty($"{type} added");
        RebuildWorkspace();
        RebuildExplorer();
        SelectPart(map.Objects.Count - 1);
    }

    private void AddEmptyModel()
    {
        PushUndo();
        map.Objects.Add(new NovusPart { Id = Guid.NewGuid().ToString("N"), Type = "Model", Name = "Model", Position = Vector3.Zero, Size = Vector3.One, Color = Colors.White, CanCollide = false });
        MarkDirty("Model added");
        RebuildWorkspace();
        RebuildExplorer();
        SelectPart(map.Objects.Count - 1);
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

    private void AddSimpleHouse()
    {
        PushUndo();
        var modelId = Guid.NewGuid().ToString("N");
        map.Objects.Add(new NovusPart { Id = modelId, Type = "Model", Name = "Simple House", Position = Vector3.Zero, CanCollide = false });
        map.Objects.Add(new NovusPart { Id = Guid.NewGuid().ToString("N"), ParentId = modelId, Type = "Part", Name = "House Walls", Position = new Vector3(0, 2, 0), Size = new Vector3(12, 4, 10), Color = new Color(0.7f, 0.42f, 0.22f), Material = "Wood" });
        map.Objects.Add(new NovusPart { Id = Guid.NewGuid().ToString("N"), ParentId = modelId, Type = "Wedge", Name = "Roof A", Position = new Vector3(-3, 5.2f, 0), Rotation = new Vector3(0, 0, 0), Size = new Vector3(8, 3, 11), Color = new Color(0.55f, 0.08f, 0.06f), Material = "Brick" });
        map.Objects.Add(new NovusPart { Id = Guid.NewGuid().ToString("N"), ParentId = modelId, Type = "Wedge", Name = "Roof B", Position = new Vector3(3, 5.2f, 0), Rotation = new Vector3(0, 180, 0), Size = new Vector3(8, 3, 11), Color = new Color(0.55f, 0.08f, 0.06f), Material = "Brick" });
        MarkDirty("House model added");
        RebuildWorkspace();
        RebuildExplorer();
        SelectPart(map.Objects.FindIndex(p => p.Id == modelId));
    }

    private void AddTree()
    {
        PushUndo();
        var modelId = Guid.NewGuid().ToString("N");
        map.Objects.Add(new NovusPart { Id = modelId, Type = "Model", Name = "Tree", Position = Vector3.Zero, CanCollide = false });
        map.Objects.Add(new NovusPart { Id = Guid.NewGuid().ToString("N"), ParentId = modelId, Type = "Cylinder", Name = "Trunk", Position = new Vector3(0, 2, 0), Size = new Vector3(2, 4, 2), Color = new Color(0.38f, 0.2f, 0.08f), Material = "Wood" });
        map.Objects.Add(new NovusPart { Id = Guid.NewGuid().ToString("N"), ParentId = modelId, Type = "Ball", Name = "Leaves", Position = new Vector3(0, 5.2f, 0), Size = new Vector3(6, 6, 6), Color = new Color(0.1f, 0.48f, 0.14f), Material = "Grass" });
        MarkDirty("Tree model added");
        RebuildWorkspace();
        RebuildExplorer();
        SelectPart(map.Objects.FindIndex(p => p.Id == modelId));
    }

    private void AddStone()
    {
        AddPresetPart("Stone", new Vector3(0, 1.1f, 0), new Vector3(4, 2.2f, 3), new Color(0.45f, 0.48f, 0.5f), "Stone");
        if (selectedPart >= 0) map.Objects[selectedPart].Type = "Ball";
        RebuildWorkspace();
        RebuildExplorer();
    }

    private void ResetClassicBaseplate()
    {
        PushUndo();
        map.Objects.Clear();
        NovusApi.EnsurePlayable(map);
        map.Spawn = new Vector3(0, 4, 0);
        MarkDirty("Workspace cleared");
        RebuildWorkspace();
        RebuildExplorer();
        SelectPart(0);
    }

    private void DuplicateSelected()
    {
        if (selectedParts.Count == 0 && (selectedPart < 0 || selectedPart >= map.Objects.Count)) return;
        PushUndo();
        var indices = selectedParts.Count > 0 ? selectedParts.ToArray() : new[] { selectedPart };
        selectedParts.Clear();
        foreach (var index in indices)
        {
            if (index < 0 || index >= map.Objects.Count) continue;
            var copy = ClonePart(map.Objects[index]);
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name += " Copy";
            copy.Position += new Vector3(2, 0, 2);
            map.Objects.Add(copy);
            selectedParts.Add(map.Objects.Count - 1);
        }
        selectedPart = selectedParts.Count > 0 ? selectedParts[^1] : -1;
        MarkDirty("Duplicated");
        RebuildWorkspace();
        RebuildExplorer();
        RefreshProperties();
        UpdateSelectionBox();
    }

    private void DeleteSelected()
    {
        if (selectedParts.Count > 0)
        {
            PushUndo();
            selectedParts.Sort();
            for (var i = selectedParts.Count - 1; i >= 0; i--)
                if (selectedParts[i] >= 0 && selectedParts[i] < map.Objects.Count)
                    map.Objects.RemoveAt(selectedParts[i]);
            selectedParts.Clear();
            selectedPart = -1;
            MarkDirty("Deleted selection");
        }
        else if (selectedScript >= 0 && selectedScript < map.Scripts.Count)
        {
            PushUndo();
            map.Scripts.RemoveAt(selectedScript);
            selectedScript = -1;
            MarkDirty("Deleted script");
        }
        RebuildWorkspace();
        RebuildExplorer();
        RefreshProperties();
        UpdateSelectionBox();
    }

    private void CopySelected()
    {
        clipboard.Clear();
        foreach (var index in selectedParts)
            if (index >= 0 && index < map.Objects.Count)
                clipboard.Add(ClonePart(map.Objects[index]));
        if (clipboard.Count == 0 && selectedPart >= 0 && selectedPart < map.Objects.Count)
            clipboard.Add(ClonePart(map.Objects[selectedPart]));
        Log($"Copied {clipboard.Count} object(s).");
    }

    private void PasteClipboard()
    {
        if (clipboard.Count == 0) return;
        PushUndo();
        selectedParts.Clear();
        foreach (var src in clipboard)
        {
            var copy = ClonePart(src);
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name += " Paste";
            copy.Position += new Vector3(3, 0, 3);
            map.Objects.Add(copy);
            selectedParts.Add(map.Objects.Count - 1);
        }
        selectedPart = selectedParts[^1];
        MarkDirty("Pasted");
        RebuildWorkspace();
        RebuildExplorer();
        RefreshProperties();
        UpdateSelectionBox();
    }

    private void SelectAllParts()
    {
        selectedParts.Clear();
        for (var i = 0; i < map.Objects.Count; i++) selectedParts.Add(i);
        selectedPart = selectedParts.Count > 0 ? selectedParts[^1] : -1;
        selectedScript = -1;
        RefreshProperties();
        UpdateSelectionBox();
    }

    private void GroupSelected()
    {
        if (selectedParts.Count < 2) return;
        PushUndo();
        var model = new NovusPart { Id = Guid.NewGuid().ToString("N"), Type = "Model", Name = "Model", Position = AverageSelectedPosition(), CanCollide = false };
        map.Objects.Add(model);
        foreach (var index in selectedParts)
            if (index >= 0 && index < map.Objects.Count)
                map.Objects[index].ParentId = model.Id;
        selectedParts.Clear();
        selectedPart = map.Objects.Count - 1;
        selectedParts.Add(selectedPart);
        MarkDirty("Grouped selection");
        RebuildWorkspace();
        RebuildExplorer();
        RefreshProperties();
        UpdateSelectionBox();
    }

    private void UngroupSelected()
    {
        if (selectedPart < 0 || selectedPart >= map.Objects.Count) return;
        var model = map.Objects[selectedPart];
        PushUndo();
        foreach (var part in map.Objects)
            if (part.ParentId == model.Id) part.ParentId = model.ParentId;
        if (model.Type == "Model") map.Objects.RemoveAt(selectedPart);
        selectedParts.Clear();
        selectedPart = -1;
        MarkDirty("Ungrouped");
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
            if (publish) map.ThumbnailUrl = CaptureThumbnail();
            map.GameId = await NovusApi.SaveStudioProject(baseUrl, ticket, map, publish);
            dirty = false;
            status.Text = publish ? $"Publicado no site como jogo {map.GameId}." : $"Rascunho salvo no site como jogo {map.GameId}.";
            UpdateWindowTitle();
            Log(status.Text);
        }
        catch (Exception ex)
        {
            status.Text = "Erro ao salvar: " + ex.Message;
            Log(status.Text);
        }
    }

    private void TestGame()
    {
        if (map.GameId <= 0)
        {
            SaveProject(false);
            Log("Teste preparado: salve o rascunho e clique Test de novo para abrir pelo site.");
            return;
        }
        var url = $"{baseUrl.TrimEnd('/')}/game.html?id={map.GameId}";
        Log("Abrindo teste: " + url);
        OS.ShellOpen(url);
    }

    private string CaptureThumbnail()
    {
        try
        {
            var image = GetViewport().GetTexture().GetImage();
            image.Resize(320, 160, Image.Interpolation.Nearest);
            var png = image.SavePngToBuffer();
            return "data:image/png;base64," + Convert.ToBase64String(png);
        }
        catch (Exception ex)
        {
            Log("Thumbnail capture failed: " + ex.Message);
            return "";
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
            ["thumbnail_url"] = map.ThumbnailUrl,
            ["maxPlayers"] = map.MaxPlayers,
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
        map.ThumbnailUrl = root.TryGetProperty("thumbnail_url", out var thumbElement) ? thumbElement.GetString() ?? "" : "";
        if (root.TryGetProperty("maxPlayers", out var maxElement) && maxElement.TryGetInt32(out var max)) map.MaxPlayers = Mathf.Clamp(max, 1, 20);
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
        PushUndo();
        var delta = Vector3.Zero;
        if (key == Key.Left) delta.X -= amount;
        if (key == Key.Right) delta.X += amount;
        if (key == Key.Up) delta.Z -= amount;
        if (key == Key.Down) delta.Z += amount;
        if (key == Key.Pageup) delta.Y += amount;
        if (key == Key.Pagedown) delta.Y -= amount;
        foreach (var index in selectedParts.Count > 0 ? selectedParts : new List<int> { selectedPart })
        {
            if (index < 0 || index >= map.Objects.Count || map.Objects[index].Locked) continue;
            var part = map.Objects[index];
            if (mode == ToolMode.Move || mode == ToolMode.Select) part.Position = Snap(part.Position + delta);
            if (mode == ToolMode.Rotate) part.Rotation += delta * 5f;
            if (mode == ToolMode.Scale) part.Size = new Vector3(Mathf.Max(0.1f, part.Size.X + delta.X), Mathf.Max(0.1f, part.Size.Y + delta.Y), Mathf.Max(0.1f, part.Size.Z + delta.Z));
        }
        MarkDirty("Nudged");
        RebuildWorkspace();
        RefreshProperties();
    }

    private void NudgeSelectionAxis(Vector3 delta)
    {
        if (selectedPart < 0 || selectedPart >= map.Objects.Count) return;
        PushUndo();
        foreach (var index in selectedParts.Count > 0 ? selectedParts : new List<int> { selectedPart })
        {
            if (index < 0 || index >= map.Objects.Count || map.Objects[index].Locked) continue;
            var part = map.Objects[index];
            if (mode == ToolMode.Rotate) part.Rotation += delta * 15f;
            else if (mode == ToolMode.Scale)
                part.Size = new Vector3(
                    Mathf.Max(0.1f, part.Size.X + delta.X),
                    Mathf.Max(0.1f, part.Size.Y + delta.Y),
                    Mathf.Max(0.1f, part.Size.Z + delta.Z)
                );
            else part.Position = Snap(part.Position + delta);
        }
        MarkDirty("Transformed");
        RebuildWorkspace();
        RefreshProperties();
        UpdateSelectionBox();
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
        RefreshProperties();
    }

    private void MarkDirty(string action)
    {
        dirty = true;
        Log(action);
        UpdateWindowTitle();
    }

    private void UpdateWindowTitle()
    {
        var text = $"Novus Worlds Studio - {map.Name}{(dirty ? " *" : "")}";
        DisplayServer.WindowSetTitle(text);
        if (titleLabel != null) titleLabel.Text = text;
    }

    private void PushUndo()
    {
        undo.Push(Snapshot());
        while (undo.Count > 50)
        {
            var keep = undo.ToArray();
            undo.Clear();
            for (var i = Mathf.Min(49, keep.Length - 1); i >= 0; i--) undo.Push(keep[i]);
        }
        redo.Clear();
    }

    private string Snapshot()
    {
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["title"] = map.Name,
            ["description"] = map.Description,
            ["gameId"] = map.GameId,
            ["map"] = NovusApi.ToWireMap(map)
        });
    }

    private void RestoreSnapshot(string json)
    {
        LoadProjectJson(json);
        selectedPart = -1;
        selectedScript = -1;
        selectedParts.Clear();
        RebuildWorkspace();
        RebuildExplorer();
        RefreshProperties();
        UpdateSelectionBox();
        dirty = true;
        UpdateWindowTitle();
    }

    private void Undo()
    {
        if (undo.Count == 0) return;
        redo.Push(Snapshot());
        RestoreSnapshot(undo.Pop());
        Log("Undo");
    }

    private void Redo()
    {
        if (redo.Count == 0) return;
        undo.Push(Snapshot());
        RestoreSnapshot(redo.Pop());
        Log("Redo");
    }

    private static NovusPart ClonePart(NovusPart src) => new()
    {
        Id = src.Id,
        Type = src.Type,
        ParentId = src.ParentId,
        Name = src.Name,
        Position = src.Position,
        Rotation = src.Rotation,
        Size = src.Size,
        Color = src.Color,
        Material = src.Material,
        Anchored = src.Anchored,
        CanCollide = src.CanCollide,
        Locked = src.Locked,
        Visible = src.Visible,
        CastShadow = src.CastShadow,
        Transparency = src.Transparency,
        Reflectance = src.Reflectance,
        Brightness = src.Brightness,
        Range = src.Range
    };

    private Vector3 AverageSelectedPosition()
    {
        var sum = Vector3.Zero;
        var count = 0;
        foreach (var index in selectedParts)
            if (index >= 0 && index < map.Objects.Count)
            {
                sum += map.Objects[index].Position;
                count++;
            }
        return count == 0 ? Vector3.Zero : sum / count;
    }

    private NovusPart? copiedProperties;

    private void CopyPropertiesFromSelected()
    {
        if (selectedPart < 0 || selectedPart >= map.Objects.Count) return;
        copiedProperties = ClonePart(map.Objects[selectedPart]);
        Log("Properties copied.");
    }

    private void PastePropertiesToSelected()
    {
        if (copiedProperties == null || selectedPart < 0 || selectedPart >= map.Objects.Count) return;
        PushUndo();
        foreach (var index in selectedParts.Count > 0 ? selectedParts : new List<int> { selectedPart })
        {
            if (index < 0 || index >= map.Objects.Count || map.Objects[index].Locked) continue;
            var target = map.Objects[index];
            target.Size = copiedProperties.Size;
            target.Color = copiedProperties.Color;
            target.Material = copiedProperties.Material;
            target.Anchored = copiedProperties.Anchored;
            target.CanCollide = copiedProperties.CanCollide;
            target.Transparency = copiedProperties.Transparency;
            target.Reflectance = copiedProperties.Reflectance;
            target.CastShadow = copiedProperties.CastShadow;
        }
        MarkDirty("Properties pasted");
        RebuildWorkspace();
        RefreshProperties();
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
        StyleButton(button);
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    private static Button AddToolButton(Container parent, string text, string tooltip, Action action)
    {
        var button = AddButton(parent, text, action);
        button.TooltipText = tooltip;
        return button;
    }

    private static void StyleButton(Button button)
    {
        var normal = new StyleBoxFlat { BgColor = new Color(0.34f, 0.4f, 0.46f), BorderColor = new Color(0.72f, 0.78f, 0.82f), BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2, CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2 };
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = new Color(0.45f, 0.54f, 0.62f);
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeColorOverride("font_color", Colors.White);
    }

    private static Label Header(string text) => new() { Text = text, Modulate = new Color(0.82f, 0.92f, 1f), HorizontalAlignment = HorizontalAlignment.Left };

    private static Panel Panel(Vector2 position, Vector2 size, Color color)
    {
        var panel = new Panel { Position = position, Size = size };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = color, BorderColor = new Color(0.4f, 0.58f, 0.74f), BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1 });
        return panel;
    }

    private void AddLineEdit(Container parent, string label, string value, Action<string> changed)
    {
        var box = new HBoxContainer();
        box.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(92, 0), Modulate = Colors.White });
        var input = new LineEdit { Text = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        input.TextSubmitted += text => { if (!updatingProperties) changed(text); };
        input.FocusExited += () => { if (!updatingProperties) changed(input.Text); };
        box.AddChild(input);
        parent.AddChild(box);
    }

    private void AddVector(Container parent, string label, Vector3 value, Action<Vector3> changed)
    {
        parent.AddChild(new Label { Text = label, Modulate = Colors.White });
        var row = new HBoxContainer();
        var xs = Spin(value.X, -2048, 2048, 0.1);
        var ys = Spin(value.Y, -2048, 2048, 0.1);
        var zs = Spin(value.Z, -2048, 2048, 0.1);
        void Apply(double _) { if (!updatingProperties) changed(new Vector3((float)xs.Value, (float)ys.Value, (float)zs.Value)); }
        xs.ValueChanged += Apply; ys.ValueChanged += Apply; zs.ValueChanged += Apply;
        row.AddChild(xs); row.AddChild(ys); row.AddChild(zs);
        parent.AddChild(row);
    }

    private void AddPalette(Container parent, NovusPart part)
    {
        var row = new GridContainer { Columns = 8 };
        var colors = new[]
        {
            "#F2F3F3","#A1C48C","#F5CD30","#D7C59A","#E29B40","#C4281C","#FFFFFF","#1B2A35",
            "#0D69AC","#008F9C","#80BBDB","#A3A2A5","#6E99CA","#B4D2E4","#4B974B","#A05F35",
            "#E8BAC8","#DA867A","#7C5C46","#6B327C","#EAB892","#7B2E2F","#2B6A38","#4C5B5C",
            "#AA5500","#FFFF00","#FF66CC","#33FFCC","#0066CC","#CCFF00","#FF3300","#999999",
            "#003366","#336699","#6699CC","#99CCFF","#663300","#996633","#CC9966","#FFCC99",
            "#330000","#660000","#990000","#CC0000","#003300","#006600","#009900","#00CC00",
            "#000033","#000066","#000099","#0000CC","#333333","#666666","#999999","#CCCCCC",
            "#FF00FF","#00FFFF","#FFD700","#8B4513","#708090","#2F4F4F","#B22222","#228B22"
        };
        foreach (var hex in colors)
        {
            var button = new Button { Text = "", CustomMinimumSize = new Vector2(18, 18), FocusMode = Control.FocusModeEnum.None };
            button.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = ParseHexColor(hex, Colors.White), BorderColor = Colors.Black, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1 });
            button.Pressed += () => { part.Color = ParseHexColor(hex, part.Color); RebuildWorkspace(); RefreshProperties(); };
            row.AddChild(button);
        }
        parent.AddChild(row);
    }

    private void AddTransformButtons(Container parent)
    {
        parent.AddChild(new Label { Text = mode == ToolMode.Rotate ? "Rotate step" : mode == ToolMode.Scale ? "Scale step" : "Move step" });
        var row1 = new HBoxContainer();
        AddButton(row1, "X-", () => NudgeSelectionAxis(new Vector3(-1, 0, 0)));
        AddButton(row1, "X+", () => NudgeSelectionAxis(new Vector3(1, 0, 0)));
        AddButton(row1, "Y-", () => NudgeSelectionAxis(new Vector3(0, -1, 0)));
        AddButton(row1, "Y+", () => NudgeSelectionAxis(new Vector3(0, 1, 0)));
        parent.AddChild(row1);
        var row2 = new HBoxContainer();
        AddButton(row2, "Z-", () => NudgeSelectionAxis(new Vector3(0, 0, -1)));
        AddButton(row2, "Z+", () => NudgeSelectionAxis(new Vector3(0, 0, 1)));
        AddButton(row2, "Focus", FocusSelected);
        AddButton(row2, "Delete", DeleteSelected);
        parent.AddChild(row2);
    }

    private void AddFloat(Container parent, string label, float value, double min, double max, Action<float> changed)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(92, 0), Modulate = Colors.White });
        var spin = Spin(value, min, max, 0.05);
        spin.ValueChanged += v => { if (!updatingProperties) changed((float)v); };
        row.AddChild(spin);
        parent.AddChild(row);
    }

    private void AddCheck(Container parent, string label, bool value, Action<bool> changed)
    {
        var check = new CheckBox { Text = label, ButtonPressed = value };
        check.AddThemeColorOverride("font_color", Colors.White);
        check.Toggled += enabled => { if (!updatingProperties) changed(enabled); };
        parent.AddChild(check);
    }

    private void AddColor(Container parent, string label, Color value, Action<Color> changed)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(92, 0), Modulate = Colors.White });
        var picker = new ColorPickerButton { Color = value, CustomMinimumSize = new Vector2(120, 24) };
        picker.ColorChanged += color => { if (!updatingProperties) changed(color); };
        row.AddChild(picker);
        parent.AddChild(row);
    }

    private void AddOption(Container parent, string label, string[] options, string value, Action<string> changed)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(92, 0), Modulate = Colors.White });
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
        if (part.Type == "Model") return "Model";
        if (part.Type == "Script") return "Script";
        if (part.Type.Contains("Light", StringComparison.OrdinalIgnoreCase)) return "Light";
        if (part.Type == "Decal") return "Decal";
        return part.Type;
    }

    private bool IsPointerOverUi(Vector2 pos)
    {
        bool Hit(Control control) => control != null && new Rect2(control.GlobalPosition, control.Size).HasPoint(pos);
        return Hit(toolbarPanel) || Hit(toolboxPanel) || Hit(explorerSearch) || Hit(explorer) || Hit(propertiesPanel) || Hit(scriptPanel) || Hit(outputPanel);
    }

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

    private static string ColorToHex(Color color)
    {
        return $"#{Mathf.Clamp((int)(color.R * 255), 0, 255):X2}{Mathf.Clamp((int)(color.G * 255), 0, 255):X2}{Mathf.Clamp((int)(color.B * 255), 0, 255):X2}";
    }
}
