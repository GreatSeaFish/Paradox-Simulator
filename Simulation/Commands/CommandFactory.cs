// Core\System\CommandSystem\CommandFactory.cs

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
            return dto.InputType switch
            {
                1 => new LaserMoveCommand { Direction = dto.MoveDirection },
                2 => new TimeSpeedCommand { SpeedLevel = dto.ActionValue },
                // 如果后续有建造、造兵指令，只需在这里加一行即可
                // 3 => new BuildCommand { TargetHex = dto.TargetHex, BuildingId = dto.ActionValue },
                // 【新增】路由到殖民指令，并塞入三维坐标
                3 => new ColonizeCommand { TargetHex = new HexCoord(dto.TargetHexX, dto.TargetHexY, dto.TargetHexZ) },
                // 【新增】路由到造兵指令，并塞入三维坐标
                4 => new BuildUnitCommand { TargetHex = new HexCoord(dto.TargetHexX, dto.TargetHexY, dto.TargetHexZ) },
                
                _ => null // 无效或空指令
            };
        }
    }
}