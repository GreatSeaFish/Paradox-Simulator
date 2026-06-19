using FixedMath.NET;
using LiteNetLib.Utils;
using Shared.Math;

namespace Shared.Protocol;
public class PlayerCommand : INetSerializable
{
    public int PlayerId { get; set; }
    public int CommandSeq { get; set; }   // 客户端自身生成的指令序号，防止乱序或用于校验
    
    // 操作类型：0-静止, 1-移动, 2-系统/时间控制
    public int InputType { get; set; }    
    
    public FixVector2 MoveDirection { get; set; } // 移动方向（定点数）

    // 【新增】附加动作参数，用于存储具体的技能ID或时间速度档位（0=暂停，1~5=速度档位）
    public int ActionValue { get; set; }  

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(PlayerId);
        writer.Put(CommandSeq);
        writer.Put(InputType);
        
        // Fix64 底层通常是 long (RawValue)，直接传 long 效率最高且无损
        writer.Put(MoveDirection.X.RawValue);
        writer.Put(MoveDirection.Y.RawValue);

        // 【新增】序列化附加参数
        writer.Put(ActionValue);
    }

    public void Deserialize(NetDataReader reader)
    {
        PlayerId = reader.GetInt();
        CommandSeq = reader.GetInt();
        InputType = reader.GetInt();
        
        long xRaw = reader.GetLong();
        long yRaw = reader.GetLong();
        MoveDirection = new FixVector2(Fix64.FromRaw(xRaw), Fix64.FromRaw(yRaw));

        // 【新增】反序列化附加参数
        ActionValue = reader.GetInt();
    }
}