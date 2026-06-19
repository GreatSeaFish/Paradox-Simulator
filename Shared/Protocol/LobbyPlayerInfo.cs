using LiteNetLib.Utils;

namespace Shared.Protocol
{
    public class LobbyPlayerInfo : INetSerializable
    {
        public int PlayerId { get; set; }
        public string Nickname { get; set; } = string.Empty;
        
        // 0-7 对应下拉菜单的索引（即位置 1-8）
        public int SlotId { get; set; }  
        
        // 0-7 对应 8 种颜色
        public int ColorId { get; set; } 
        
        public bool IsReady { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(PlayerId);
            writer.Put(Nickname);
            writer.Put(SlotId);
            writer.Put(ColorId);
            writer.Put(IsReady);
        }

        public void Deserialize(NetDataReader reader)
        {
            PlayerId = reader.GetInt();
            Nickname = reader.GetString();
            SlotId = reader.GetInt();
            ColorId = reader.GetInt();
            IsReady = reader.GetBool();
        }
    }
}