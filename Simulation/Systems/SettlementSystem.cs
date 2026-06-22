using System;
using System.Linq;
using System.Collections.Generic; // 新增引入
using ParadoxSimulator.Simulation.State;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem; // 新增引入

namespace ParadoxSimulator.Simulation.Systems
{
    public class SettlementSystem
    {
        private readonly WorldSimulationState _state;

        public SettlementSystem(WorldSimulationState state)
        {
            _state = state;
            _state.OnDailySettlementRequired += ExecuteDailySettlement;
            _state.OnMonthlySettlementRequired += ExecuteMonthlySettlement;
        }
        
        /// <summary>
        /// 由 TimeSystem 驱动的每日业务结算
        /// </summary>
        public void ExecuteDailySettlement()
        {
            // 【新增】处理所有正在进行的殖民任务倒计时
            var completedTasks = new List<HexCoord>();
            
            foreach (var kvp in _state.ActiveColonizations)
            {
                var task = kvp.Value;
                task.RemainingDays--;
                
                if (task.RemainingDays <= 0)
                {
                    completedTasks.Add(kvp.Key);
                }
            }

            // 处理所有在今天彻底完成殖民的地块
            foreach (var coord in completedTasks)
            {
                int ownerId = _state.ActiveColonizations[coord].PlayerId;
                
                // 1. 正式划归领地权
                _state.SetTileOwner(coord, ownerId);
                
                // 2. 任务完成，从正在进行的队列中移除
                _state.ActiveColonizations.Remove(coord); 
                
                // 3. 由于领地变多了，且无需再交该地块的维护费，重新计算该玩家的月度预期收入
                RecalculateMonthlyIncome(ownerId);
            }
        }

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
        
        // 【核心修改】重新计算月度预期收支（涵盖基础产出与维护费开销）
        public void RecalculateMonthlyIncome(int playerId)
        {
            // 基础收入：每个占领成功的地块产出 2 资金
            var tileCount = _state.TileOwners.Count(kvp => kvp.Value == playerId);
            int expectedIncome = tileCount * 2;
            
            // 维护费支出：每个活跃的殖民地任务每月扣除 1 资金
            var activeColonizationsCount = _state.ActiveColonizations.Count(kvp => kvp.Value.PlayerId == playerId);
            expectedIncome -= (activeColonizationsCount * 1);
            
            _state.SetMonthlyFundsChange(playerId, expectedIncome);
        }
    }
}