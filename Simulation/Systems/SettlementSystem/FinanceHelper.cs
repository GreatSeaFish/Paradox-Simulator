using System.Linq;
using ParadoxSimulator.Simulation.State;
using ParadoxSimulator.Simulation.State.WorldModel;
namespace ParadoxSimulator.Simulation.Systems.SettlementSystem
{
    public static class FinanceHelper
    {
        /// <summary>
        /// 核心修改：重新计算月度预期收支（涵盖基础产出与维护费开销）
        /// </summary>
        public static void RecalculateMonthlyIncome(WorldSimulationState state, int playerId)
        {
            // 基础收入：每个占领成功的地块产出 2 资金
            var tileCount = state.TileOwners.Count(kvp => kvp.Value == playerId);
            int expectedIncome = tileCount * 2;
            
            // 维护费支出：每个活跃的殖民地任务每月扣除 1 资金
            var activeColonizationsCount = state.ActiveColonizations.Count(kvp => kvp.Value.PlayerId == playerId);
            expectedIncome -= (activeColonizationsCount * 1);
            
            state.SetMonthlyFundsChange(playerId, expectedIncome);
        }
    }
}