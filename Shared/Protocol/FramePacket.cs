using LiteNetLib.Utils;


namespace Shared.Protocol;

public class FramePacket : INetSerializable
{
    public int FrameId { get; set; } // 全局逻辑帧号
    public List<PlayerCommand> Commands { get; set; } = new List<PlayerCommand>();

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(FrameId);
        
        // 写入指令的数量
        writer.Put(Commands.Count);
        foreach (var cmd in Commands)
        {
            // 利用 PlayerCommand 自身的序列化方法
            cmd.Serialize(writer);
        }
    }

    public void Deserialize(NetDataReader reader)
    {
        FrameId = reader.GetInt();
        
        int count = reader.GetInt();
        Commands.Clear();
        for (int i = 0; i < count; i++)
        {
            var cmd = new PlayerCommand();
            cmd.Deserialize(reader);
            Commands.Add(cmd);
        }
    }
}