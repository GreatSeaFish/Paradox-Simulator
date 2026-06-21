// Core\System\CommandSystem\Commands\MoveCommand.cs
using FixedMath.NET;
using ParadoxSimulator.Core.GameData;
using Shared.Math;

namespace ParadoxSimulator.Core.CommandSystem.Commands
{
    public class MoveCommand : IGameCommand
    {
        public FixVector2 Direction { get; set; }

        public void Execute(WorldSimulationState state, TimeSystem timeSystem, int playerId)
        {
            // 原有逻辑：更新玩家位置
            if (state.PlayerPositions.ContainsKey(playerId))
            {
                state.PlayerPositions[playerId] += Direction * Fix64.One;
            }
        }
    }
}