// Core\System\CommandSystem\IGameCommand.cs
using ParadoxSimulator.Core.GameData;

namespace ParadoxSimulator.Core.CommandSystem
{
    public interface IGameCommand
    {
        /// <summary>
        /// 执行具体的业务逻辑
        /// </summary>
        /// <param name="state">世界模拟状态仓库</param>
        /// <param name="timeManager">时间管理器</param>
        /// <param name="playerId">发出该指令的玩家ID</param>
        void Execute(WorldSimulationState state, TimeManager timeManager, int playerId);
    }
}