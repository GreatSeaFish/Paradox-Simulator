using ParadoxSimulator.Simulation.Systems.WorldMapSystem;
using Shared.Protocol;

namespace ParadoxSimulator.Simulation.Commands
{
    public static class CommandFactory
    {
        /// <summary>
        /// 根据网络数据包生成具体的逻辑指令
        /// </summary>
        public static IGameCommand? Create(PlayerCommand dto)
        {
            // 【修改】：直接使用枚举进行模式匹配
            return dto.InputType switch
            {
                CommandType.Move => new LaserMoveCommand { Direction = dto.MoveDirection },
                
                CommandType.TimeSpeedControl => new TimeSpeedCommand { SpeedLevel = dto.ActionValue },
                
                CommandType.Colonize => new ColonizeCommand { TargetHex = new HexCoord(dto.TargetHexX, dto.TargetHexY, dto.TargetHexZ) },
                
                CommandType.BuildUnit => new BuildUnitCommand { TargetHex = new HexCoord(dto.TargetHexX, dto.TargetHexY, dto.TargetHexZ) },
                
                // 找到 CommandType.UnitMove 分支并替换为：
                CommandType.UnitMove => new UnitMoveCommand 
                { 
                    UnitId = dto.ActionValue, // 【关联修改】：将网络层的 ID 传递给逻辑指令
                    TargetHex = new HexCoord(dto.TargetHexX, dto.TargetHexY, dto.TargetHexZ) 
                },
                
                _ => null // 无效或空指令
            };
        }
    }
}