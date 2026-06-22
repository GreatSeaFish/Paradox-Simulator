// Core\System\CommandSystem\Commands\TimeSpeedCommand.cs

using ParadoxSimulator.Simulation.State;
using ParadoxSimulator.Simulation.Systems;

namespace ParadoxSimulator.Simulation.Commands
{
    public class TimeSpeedCommand : IGameCommand
    {
        public int SpeedLevel { get; set; }

        public void Execute(WorldSimulationState state, int playerId)
        {
            // 原有逻辑：设置时间流速
            state.SetTimeSpeed(SpeedLevel);
        }
    }
}