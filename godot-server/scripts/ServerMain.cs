using Godot;
using System.Collections.Generic;

public partial class ServerMain : Node
{
    private readonly Dictionary<long, PlayerState> players = new();

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
        players[id] = new PlayerState { Position = position, Rotation = rotation, Animation = animation };
        Rpc(nameof(ReceiveState), id, position, rotation, animation);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    public void ReceiveState(long id, Vector3 position, Vector3 rotation, string animation) {}

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void SendChat(string message)
    {
        var id = Multiplayer.GetRemoteSenderId();
        Rpc(nameof(ReceiveChat), id, message.Left(120));
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveChat(long id, string message) {}

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
    }
}
