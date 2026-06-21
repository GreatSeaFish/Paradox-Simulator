// Core\System\CommandSystem\Commands\TimeSpeedCommand.cs
using ParadoxSimulator.Core.GameData;

namespace ParadoxSimulator.Core.CommandSystem.Commands
{
    public class TimeSpeedCommand : IGameCommand
    {
        public int SpeedLevel { get; set; }

        public void Execute(WorldSimulationState state, TimeManager timeManager, int playerId)
        {
            // 原有逻辑：设置时间流速
            timeManager.SetTimeSpeed(SpeedLevel);
        }
    }
}