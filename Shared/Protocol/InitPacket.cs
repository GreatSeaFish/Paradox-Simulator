using LiteNetLib.Utils;

namespace Shared.Protocol
{
    public class InitPacket : INetSerializable
    {
        public int AssignedPlayerId { get; set; }
        public int TotalPlayersRequired { get; set; }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(AssignedPlayerId);
            writer.Put(TotalPlayersRequired);
        }

        public void Deserialize(NetDataReader reader)
        {
            AssignedPlayerId = reader.GetInt();
            TotalPlayersRequired = reader.GetInt();
        }
    }
}