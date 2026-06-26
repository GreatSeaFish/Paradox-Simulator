using FixedMath.NET;
using LiteNetLib.Utils;
using Shared.Math;

namespace Shared.Protocol;

public class PlayerCommand : INetSerializable
{
    public int PlayerId { get; set; }
    public int CommandSeq { get; set; } 
    
    // 【修改】：使用枚举替换原生 short
    public CommandType InputType { get; set; }
    
    // ---------------- 数据载荷区 ----------------
    public FixVector2 MoveDirection { get; set; }
    public int ActionValue { get; set; }  
    
    public short TargetHexX { get; set; }
    public short TargetHexY { get; set; }
    public short TargetHexZ { get; set; }
    
    public short SourceHexX { get; set; }
    public short SourceHexY { get; set; }
    public short SourceHexZ { get; set; }
    
    public void Serialize(NetDataWriter writer)
    {
        writer.Put(PlayerId);
        writer.Put(CommandSeq);
        
        // 【修改】：强转为 short 写入 2 bytes
        writer.Put((short)InputType); 
        
        // 【修改】：使用枚举进行路由，告别魔法数字
        switch (InputType)
        {
            case CommandType.Move: 
                writer.Put(MoveDirection.X.RawValue);
                writer.Put(MoveDirection.Y.RawValue);
                break;
                
            case CommandType.TimeSpeedControl: 
                writer.Put(ActionValue);
                break;
                
            case CommandType.Colonize: 
            case CommandType.BuildUnit:  
                writer.Put(TargetHexX);
                writer.Put(TargetHexY);
                writer.Put(TargetHexZ);
                break;
            // 在 Serialize 方法的 switch 中新增分支：
            case CommandType.UnitMove:
                writer.Put(ActionValue); // 【核心】：借用 ActionValue 写入唯一的部队 ID
                writer.Put(TargetHexX); writer.Put(TargetHexY); writer.Put(TargetHexZ);
                break;
            case CommandType.MergeUnits: // 【新增】路由进相同的坐标写入逻辑 [cite: 119]
                writer.Put(TargetHexX);
                writer.Put(TargetHexY);
                writer.Put(TargetHexZ);
                break;
        }
    }

    public void Deserialize(NetDataReader reader)
    {
        PlayerId = reader.GetInt();
        CommandSeq = reader.GetInt();
        
        // 【修改】：读取 2 bytes 并强转回枚举
        InputType = (CommandType)reader.GetShort(); 
        
        switch (InputType)
        {
            case CommandType.Move:
                long xRaw = reader.GetLong();
                long yRaw = reader.GetLong();
                MoveDirection = new FixVector2(Fix64.FromRaw(xRaw), Fix64.FromRaw(yRaw));
                break;
                
            case CommandType.TimeSpeedControl:
                ActionValue = reader.GetInt();
                break;
                
            case CommandType.Colonize:
            case CommandType.BuildUnit:
                TargetHexX = reader.GetShort();
                TargetHexY = reader.GetShort();
                TargetHexZ = reader.GetShort();
                break;
            // 在 Deserialize 方法的 switch 中新增分支：
            case CommandType.UnitMove:
                ActionValue = reader.GetInt(); // 【核心】：读取部队 ID
                TargetHexX = reader.GetShort(); TargetHexY = reader.GetShort(); TargetHexZ = reader.GetShort();
                break;
            case CommandType.MergeUnits: // 【新增】路由进相同的坐标读取逻辑 [cite: 127]
                TargetHexX = reader.GetShort();
                TargetHexY = reader.GetShort();
                TargetHexZ = reader.GetShort();
                break;
        }
    }
}    