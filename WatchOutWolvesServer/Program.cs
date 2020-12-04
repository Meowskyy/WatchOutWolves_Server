using System;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using MagicOnion;
using MagicOnion.Hosting;
using MagicOnion.Server;
using MagicOnion.Server.Hubs;
using MessagePack;
using Microsoft.Extensions.Hosting;
using UnityEngine;
//using MySql.Data.MySqlClient;

// Definition of server-to-client communication
public interface IGamingHubReceiver
{
    // return type should be `void` or `Task`, parameters are free.
    void OnJoin(PlayerData player);
    void OnLeave(PlayerData player);
    void OnReceiveLobbyInfo(PlayerData[] connectedPlayers);
    void OnGameStart(uint seed, Vector2Int worldSize);
    void OnPlayerLoaded(PlayerData player);
    void OnSendMessage(PlayerData player, string message);
    void OnPositionChange(PlayerData player);
    void OnJump(PlayerData player, Vector3 force, float jumpStartSpeed);
    void OnCrouch(PlayerData player, bool isCrouching);
    void OnAbilityUsed(PlayerData player, int abilityID);
    void OnPlayerHit(PlayerData player, Vector3 direction);
    void OnPlayerDeath(PlayerData player);
    void OnPlayerWantMonsterChange(PlayerData player, bool value);
}

// Definition of client-to-server communication
public interface IGamingHub : IStreamingHub<IGamingHub, IGamingHubReceiver>
{
    Task<PlayerData[]> JoinAsync(string roomName, string userName);
    Task LeaveAsync();
    Task StartGameAsync(uint seed, Vector2Int worldSize);
    Task GameLoadedAsync();
    Task SendMessageAsync(string message);
    Task UpdatePositionAsync(Vector3 position);
    Task JumpAsync(Vector3 force, float jumpStartSpeed);
    Task CrouchAsync(bool isCrouching);
    Task UseAbilityAsync(int abilityID);
    Task HitPlayerAsync(PlayerData player, Vector3 direction);
    Task ToggleMonsterAsync(bool value);
}

public class RoomSettings
{
    public string hostName;

}

// Custom object to be used for both sending and receiving communication
[MessagePackObject]
public class PlayerData
{
    [Key(0)]
    public string Name { get; set; }
    [Key(1)]
    public Guid Uuid { get; set; }
    [Key(2)]
    public Vector3 Position { get; set; }
    [Key(3)]
    public bool IsHost { get; set; }
    [Key(4)]
    public bool WantsMonster { get; set; } // Does the player want to be a monster?
}

//Server implementation
public class GamingHub : StreamingHubBase<IGamingHub, IGamingHubReceiver>, IGamingHub
{
    IGroup room;
    PlayerData self;
    IInMemoryStorage<PlayerData> storage;

    public async Task<PlayerData[]> JoinAsync(string roomName, string userName)
    {
        self = new PlayerData() { 
            Name = userName,
            Uuid = Context.ContextId
        };

        (room, storage) = await Group.AddAsync(roomName, self);

        PlayerData[] connectedPlayers = storage.AllValues.ToArray();

        bool isUserHost = (connectedPlayers.Length == 1);
        self.IsHost = isUserHost;
        Console.WriteLine(userName + " / " + self.Uuid + " connected to + " + roomName + "! isHost = " + isUserHost);

        BroadcastToSelf(room).OnReceiveLobbyInfo(connectedPlayers);
        Broadcast(room).OnJoin(self);

        return connectedPlayers;
    }

    public async Task LeaveAsync()
    {
        Console.WriteLine(self.Name + " disconnected!");

        if (self.IsHost)
        {
            // TODO: Change host on leave
        }

        await room.RemoveAsync(this.Context);
        Broadcast(room).OnLeave(self);
    }

    public async Task GameLoadedAsync()
    {
        Broadcast(room).OnPlayerLoaded(self);
    }

    public async Task SendMessageAsync(string message)
    {
        Console.WriteLine(self.Name + ": " + message);

        Broadcast(room).OnSendMessage(self, message);
    }

    public async Task UpdatePositionAsync(Vector3 position)
    {
        self.Position = position;
        Broadcast(room).OnPositionChange(self);
    }

    public async Task JumpAsync(Vector3 force, float jumpStartSpeed)
    {
        //Console.WriteLine(self.Name + " jumped");
        Broadcast(room).OnJump(self, force, jumpStartSpeed);
    }

    public async Task CrouchAsync(bool isCrouching)
    {
        Console.WriteLine(self.Name + " crouched");
        Broadcast(room).OnCrouch(self, isCrouching);
    }

    public async Task UseAbilityAsync(int abilityID)
    {
        Console.WriteLine(self.Name + " used ability ID: " + abilityID);
        Broadcast(room).OnAbilityUsed(self, abilityID);
    }

    public async Task StartGameAsync(uint seed, Vector2Int worldSize)
    {
        if (self.IsHost)
        {
            Console.WriteLine(self.Name + " started game");
            Console.WriteLine(string.Format("{0} / {1}", seed, worldSize));
            Console.WriteLine("Players: " + storage.AllValues.Count);

            PlayerData[] players = storage.AllValues.ToArray();
            for (int i = 0; i < players.Length; i++)
            {

            }

            Broadcast(room).OnGameStart(seed, worldSize);
        }
        else
        {
            Console.WriteLine(self.Name + " has been kicked");
            storage.Remove(Context.ContextId);
            await room.RemoveAsync(Context);
        }
    }

    public async Task HitPlayerAsync(PlayerData player, Vector3 direction)
    {
        Console.WriteLine(player.Name + " has been hit!");
        Broadcast(room).OnPlayerHit(player, direction);
    }

    public async Task ToggleMonsterAsync(bool value)
    {
        PlayerData host = storage.AllValues.First();
        self.WantsMonster = value;
        Console.WriteLine("Host: " + host.Name + " & " + self.Name + " wants to be monster: " + value);
        BroadcastTo(room, host.Uuid).OnPlayerWantMonsterChange(self, value);
    }
}


class Program
{
    public static string serverAddress = "192.168.1.100";
    public static int serverPort = 3000;

    /*
    static readonly MySqlConnectionStringBuilder builder = new MySqlConnectionStringBuilder
    {
        Server = "192.168.1.100",
        Database = "fishing",
        UserID = "root",
        Password = "",
        SslMode = MySqlSslMode.None
    };
    */

    static async Task Main(string[] args)
    {
        GrpcEnvironment.SetLogger(new Grpc.Core.Logging.ConsoleLogger());

        // setup MagicOnion hosting.
        var magicOnionHost = MagicOnionHost.CreateDefaultBuilder()
            .UseMagicOnion(
                new MagicOnionOptions(isReturnExceptionStackTraceInErrorDetail: true),
                new ServerPort(serverAddress, serverPort, ServerCredentials.Insecure))
            .UseConsoleLifetime()
            .Build();

        await magicOnionHost.RunAsync();
    }
}

