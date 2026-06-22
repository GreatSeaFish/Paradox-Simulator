using System;
using System.Linq;
using ParadoxSimulator.Simulation.State;

namespace ParadoxSimulator.Simulation.Systems
{
    /// <summary>
    /// 结算管理器
    /// </summary>
    public class SettlementSystem
    {
        private readonly WorldSimulationState _state;

        public SettlementSystem(WorldSimulationState state)
        {
            _state = state;
            
            // 【核心重构】：挂载底层状态中心抛出的结算信号
            _state.OnDailySettlementRequired += ExecuteDailySettlement;
            _state.OnMonthlySettlementRequired += ExecuteMonthlySettlement;
        }
        
        /// <summary>
        /// 由 TimeSystem 驱动的每日业务结算
        /// </summary>
        public void ExecuteDailySettlement()
        {
            // TODO: 在这里处理每日流失、资源产出等与回合/阵营相关的业务
        }

        /// <summary>
        /// 由 TimeSystem 驱动的每月业务结算
        /// </summary>
        private void ExecuteMonthlySettlement()
        {
            foreach (var playerId in _state.PlayerFunds.Keys.ToList())
            {
                int monthlyNetChange = _state.PlayerMonthlyFundsChange.TryGetValue(playerId, out int change) ? change : 0;
                
                _state.AddFundsRealtime(playerId, monthlyNetChange);
                ClientDebugger.LogHandler?.Invoke($"[SettlementSystem] 玩家 ID:{playerId} 月底结算完毕。本月净变化:{monthlyNetChange}，当前总资产:{_state.PlayerFunds[playerId]}");
                
                RecalculateMonthlyIncome(playerId);
            }
        }
        
        // 重新计算月度收入
        public void RecalculateMonthlyIncome(int playerId)
        {
            var tileCount = _state.TileOwners.Count(kvp => kvp.Value == playerId);
            int expectedIncome = tileCount * 2;
            _state.SetMonthlyFundsChange(playerId, expectedIncome);
        }
    }
}