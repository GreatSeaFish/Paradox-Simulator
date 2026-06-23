// Core\System\CommandSystem\Commands\LaserMoveCommand.cs
using FixedMath.NET;
using ParadoxSimulator.Simulation.State;
using ParadoxSimulator.Simulation.Systems;
using Shared.Math;

namespace ParadoxSimulator.Simulation.Commands
{
    public class LaserMoveCommand : IGameCommand
    {
        public FixVector2 Direction { get; set; }

        public void Execute(WorldSimulationState state,int playerId)
        {
            // 原有逻辑：更新玩家位置
            if (state.PlayerPositions.ContainsKey(playerId))
            {
                state.PlayerPositions[playerId] += Direction * Fix64.One;
            }
        }
    }
}