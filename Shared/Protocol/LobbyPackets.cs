using System.Collections.Generic;
using LiteNetLib.Utils;

namespace Shared.Protocol
{
    // 客户端 -> 服务端：请求加入大厅
    public class ClientJoinLobbyPacket : INetSerializable
    {
        public string Nickname { get; set; } = string.Empty;

        public void Serialize(NetDataWriter writer) => writer.Put(Nickname);
        public void Deserialize(NetDataReader reader) => Nickname = reader.GetString();
    }

    // 服务端 -> 客户端：广播当前大厅所有玩家数据
    public class ServerLobbySyncPacket : INetSerializable
    {
        public List<LobbyPlayerInfo> Players { get; set; } = new List<LobbyPlayerInfo>();

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Players.Count);
            foreach (var player in Players)
            {
                player.Serialize(writer);
            }
        }

        public void Deserialize(NetDataReader reader)
        {
            int count = reader.GetInt();
            Players.Clear();
            for (int i = 0; i < count; i++)
            {
                var player = new LobbyPlayerInfo();
                player.Deserialize(reader);
                Players.Add(player);
            }
        }
    }

    // 客户端 -> 服务端：请求修改位置或颜色
    public class ClientUpdateSlotColorPacket : INetSerializable
    {
        public int SlotId { get; set; }
        public int ColorId { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(SlotId);
            writer.Put(ColorId);
        }

        public void Deserialize(NetDataReader reader)
        {
            SlotId = reader.GetInt();
            ColorId = reader.GetInt();
        }
    }

    // 客户端 -> 服务端：切换准备状态
    public class ClientToggleReadyPacket : INetSerializable
    {
        public bool IsReady { get; set; }

        public void Serialize(NetDataWriter writer) => writer.Put(IsReady);
        public void Deserialize(NetDataReader reader) => IsReady = reader.GetBool();
    }
}