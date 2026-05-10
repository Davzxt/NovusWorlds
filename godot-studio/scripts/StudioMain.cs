using Godot;
using System;
using System.Text.Json;

public partial class StudioMain : Node3D
{
    private NovusMap map = new();
    private Node3D workspace = null!;
    private Camera3D camera = null!;
    private CanvasLayer ui = null!;
    private ItemList explorer = null!;
    private Label status = null!;
    private int selected = -1;
    private string baseUrl = "http://localhost:3000";
    private string ticket = "";

    public override async void _Ready()
    {
        var args = OS.GetCmdlineArgs();
        baseUrl = ReadArg(args, "--base-url", baseUrl);
        ticket = ReadArg(args, "--ticket", "");
        try
        {
            map = ticket.Length > 0 ? await NovusApi.LoadStudioProject(baseUrl, ticket) : await NovusApi.LoadPlace(baseUrl, ReadArg(args, "--game", "1"));
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Using local studio baseplate: {ex.Message}");
            NovusApi.EnsurePlayable(map);
        }
        SetupScene();
        SetupUi();
        Rebuild();
    }

    public override void _Process(double delta)
    {
        var speed = Input.IsKeyPressed(Key.Shift) ? 40f : 18f;
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
        AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = map.SkyColor, AmbientLightEnergy = 0.65f } });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-45, -35, 0), LightEnergy = 1.8f, ShadowEnabled = true });
        camera = new Camera3D { Name = "StudioCamera", Position = new Vector3(18, 18, 18), RotationDegrees = new Vector3(-35, 45, 0), Current = true };
        AddChild(camera);
    }

    private void SetupUi()
    {
        ui = new CanvasLayer();
        AddChild(ui);
        var root = new Control { Name = "StudioUi" };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ui.AddChild(root);

        var top = new HBoxContainer { Position = new Vector2(8, 8), Size = new Vector2(800, 36) };
        root.AddChild(top);
        AddButton(top, "Parte", AddPart);
        AddButton(top, "Spawn", AddSpawn);
        AddButton(top, "Duplicar", DuplicateSelected);
        AddButton(top, "Delete", DeleteSelected);
        AddButton(top, "Salvar JSON", SaveLocalJson);
        AddButton(top, "Publicar", Publish);

        explorer = new ItemList { Position = new Vector2(1050, 54), Size = new Vector2(300, 500) };
        explorer.ItemSelected += index => { selected = (int)index; status.Text = $"Selecionado: {map.Objects[selected].Name}"; };
        root.AddChild(explorer);

        status = new Label { Text = "Novus Studio Godot", Position = new Vector2(12, 720) };
        root.AddChild(status);
    }

    private static void AddButton(Container parent, string text, Action action)
    {
        var button = new Button { Text = text };
        button.Pressed += action;
        parent.AddChild(button);
    }

    private void Rebuild()
    {
        workspace?.QueueFree();
        workspace = MapBuilder.Build(map);
        AddChild(workspace);
        explorer.Clear();
        foreach (var part in map.Objects) explorer.AddItem(part.Name);
    }

    private void AddPart()
    {
        map.Objects.Add(new NovusPart { Id = Guid.NewGuid().ToString("N"), Name = "Part", Position = new Vector3(0, 3, 0), Size = new Vector3(4, 1, 4), Color = Colors.LightGray });
        Rebuild();
    }

    private void AddSpawn()
    {
        map.Spawn = new Vector3(0, 4, 0);
        map.Objects.Add(new NovusPart { Id = Guid.NewGuid().ToString("N"), Name = "SpawnLocation", Position = new Vector3(0, 0.25f, 0), Size = new Vector3(6, 0.5f, 6), Color = new Color(0.1f, 0.9f, 0.25f) });
        Rebuild();
    }

    private void DuplicateSelected()
    {
        if (selected < 0 || selected >= map.Objects.Count) return;
        var src = map.Objects[selected];
        map.Objects.Add(new NovusPart { Id = Guid.NewGuid().ToString("N"), Name = src.Name + " Copy", Position = src.Position + new Vector3(2, 0, 2), Size = src.Size, Color = src.Color, Material = src.Material, Anchored = src.Anchored, CanCollide = src.CanCollide });
        Rebuild();
    }

    private void DeleteSelected()
    {
        if (selected < 0 || selected >= map.Objects.Count) return;
        map.Objects.RemoveAt(selected);
        selected = -1;
        Rebuild();
    }

    private void SaveLocalJson()
    {
        var json = JsonSerializer.Serialize(NovusApi.ToWireMap(map), new JsonSerializerOptions { WriteIndented = true });
        var path = ProjectSettings.GlobalizePath("user://novus-map.json");
        System.IO.File.WriteAllText(path, json);
        status.Text = "Salvo em " + path;
    }

    private void Publish()
    {
        SaveLocalJson();
        status.Text = "Publicacao no site entra no proximo passo. JSON local salvo.";
    }

    private static string ReadArg(string[] args, string name, string fallback)
    {
        for (var i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return fallback;
    }
}
