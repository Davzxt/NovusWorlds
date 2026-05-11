using Godot;
using System;
using System.Collections.Generic;

public partial class ServerMain : Node
{
    private readonly Dictionary<long, PlayerState> players = new();
    private readonly List<string> chatHistory = new();

    public override void _Ready()
    {
        var port = ReadIntArg(OS.GetCmdlineArgs(), "--port", 53640);
        var maxPlayers = ReadIntArg(OS.GetCmdlineArgs(), "--max-players", 32);
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateServer(port, maxPlayers);
        if (err != Error.Ok)
        {
            GD.PrintErr($"Failed to start Novus Godot server on {port}: {err}");
            GetTree().Quit(1);
            return;
        }
        Multiplayer.MultiplayerPeer = peer;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        GD.Print($"Novus Godot server listening on {port}, max players {maxPlayers}");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    public void SubmitState(Vector3 position, Vector3 rotation, string animation)
    {
        var id = Multiplayer.GetRemoteSenderId();
        if (!players.TryGetValue(id, out var state))
        {
            state = new PlayerState();
            players[id] = state;
        }
        state.Position = position;
        state.Rotation = rotation;
        state.Animation = animation;
        Rpc(nameof(ReceiveState), id, position, rotation, animation);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    public void ReceiveState(long id, Vector3 position, Vector3 rotation, string animation) {}

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void RegisterPlayer(string username)
    {
        var id = Multiplayer.GetRemoteSenderId();
        if (!players.ContainsKey(id)) players[id] = new PlayerState();
        players[id].Username = string.IsNullOrWhiteSpace(username) ? $"Player{id}" : username.Left(20);
        if (chatHistory.Count > 0) RpcId(id, nameof(ReceiveChatHistory), string.Join("\n", chatHistory));
        Rpc(nameof(PlayerJoined), id, players[id].Username);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void PlayerJoined(long id, string username) {}

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void SendChat(string message)
    {
        var id = Multiplayer.GetRemoteSenderId();
        var clean = Moderate(message).Left(120);
        var username = players.TryGetValue(id, out var state) && !string.IsNullOrWhiteSpace(state.Username) ? state.Username : $"Player{id}";
        chatHistory.Add($"{username}: {clean}");
        if (chatHistory.Count > 50) chatHistory.RemoveAt(0);
        Rpc(nameof(ReceiveChat), id, clean);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveChat(long id, string message) {}

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveChatHistory(string history) {}

    private void OnPeerConnected(long id)
    {
        players[id] = new PlayerState();
        GD.Print($"Player connected: {id}");
    }

    private void OnPeerDisconnected(long id)
    {
        players.Remove(id);
        Rpc(nameof(PlayerLeft), id);
        GD.Print($"Player disconnected: {id}");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void PlayerLeft(long id) {}

    private static int ReadIntArg(string[] args, string name, int fallback)
    {
        for (var i = 0; i < args.Length - 1; i++) if (args[i] == name && int.TryParse(args[i + 1], out var value)) return value;
        return fallback;
    }

    private sealed class PlayerState
    {
        public Vector3 Position = Vector3.Zero;
        public Vector3 Rotation = Vector3.Zero;
        public string Animation = "idle";
        public string Username = "";
    }

    private static string Moderate(string message)
    {
        var text = (message ?? "").Trim();
        foreach (var bad in new[] { "porra", "caralho", "merda" })
            text = text.Replace(bad, "****", StringComparison.OrdinalIgnoreCase);
        return text;
    }
}
