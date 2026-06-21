// Core\System\CommandSystem\CommandFactory.cs
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
                1 => new MoveCommand { Direction = dto.MoveDirection },
                2 => new TimeSpeedCommand { SpeedLevel = dto.ActionValue },
                // 如果后续有建造、造兵指令，只需在这里加一行即可
                // 3 => new BuildCommand { TargetHex = dto.TargetHex, BuildingId = dto.ActionValue },
                
                _ => null // 无效或空指令
            };
        }
    }
}