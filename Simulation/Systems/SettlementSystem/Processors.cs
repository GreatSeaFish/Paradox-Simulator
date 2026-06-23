using System.Collections.Generic;
using System.Linq;
using ParadoxSimulator.Simulation.State;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;

namespace ParadoxSimulator.Simulation.Systems.SettlementSystem
{
    /// <summary>
    /// 殖民任务每日结算处理器
    /// </summary>
    public class ColonizationProcessor : ISettlementProcessor
    {
        public SettlementFrequency Frequency => SettlementFrequency.Daily;

        public void Execute(WorldSimulationState state)
        {
            var completedTasks = new List<HexCoord>();
            
            foreach (var kvp in state.ActiveColonizations)
            {
                var task = kvp.Value;
                task.RemainingDays--;
                
                if (task.RemainingDays <= 0)
                {
                    completedTasks.Add(kvp.Key);
                }
            }

            foreach (var coord in completedTasks)
            {
                int ownerId = state.ActiveColonizations[coord].PlayerId;
                
                // 1. 正式划归领地权
                state.SetTileOwner(coord, ownerId);
                
                // 2. 任务完成，从正在进行的队列中移除
                state.ActiveColonizations.Remove(coord); 
                
                // 3. 重新计算该玩家的月度预期收入
                FinanceHelper.RecalculateMonthlyIncome(state, ownerId);
            }
        }
    }

    /// <summary>
    /// 造兵任务每日结算处理器
    /// </summary>
    public class UnitBuildProcessor : ISettlementProcessor
    {
        public SettlementFrequency Frequency => SettlementFrequency.Daily;

        public void Execute(WorldSimulationState state)
        {
            var completedUnitBuilds = new List<HexCoord>();
            
            foreach (var kvp in state.ActiveUnitBuilds)
            {
                var task = kvp.Value;
                task.RemainingDays--;
                
                if (task.RemainingDays <= 0)
                {
                    completedUnitBuilds.Add(kvp.Key);
                }
            }

            foreach (var coord in completedUnitBuilds)
            {
                int ownerId = state.ActiveUnitBuilds[coord].PlayerId;
                
                // 1. 任务完成，从建造队列中移除
                state.ActiveUnitBuilds.Remove(coord);
                
                // 2. 部署部队并触发渲染层事件
                state.SpawnUnit(coord, ownerId, 1000);
                
                ClientDebugger.LogHandler?.Invoke($"[Settlement] 玩家 {ownerId} 在坐标({coord.X},{coord.Y},{coord.Z}) 成功招募了一支 1000 人的部队！");
            }
        }
    }

    /// <summary>
    /// 资金月底结算处理器
    /// </summary>
    public class MonthlyFundsProcessor : ISettlementProcessor
    {
        public SettlementFrequency Frequency => SettlementFrequency.Monthly;

        public void Execute(WorldSimulationState state)
        {
            // 使用 ToList() 防止循环中字典修改引起异常（虽然这里只是读键）
            foreach (var playerId in state.PlayerFunds.Keys.ToList())
            {
                int monthlyNetChange = state.PlayerMonthlyFundsChange.TryGetValue(playerId, out int change) ? change : 0;
                
                state.AddFundsRealtime(playerId, monthlyNetChange);
                ClientDebugger.LogHandler?.Invoke($"[SettlementSystem] 玩家 ID:{playerId} 月底结算完毕。本月净变化:{monthlyNetChange}，当前总资产:{state.PlayerFunds[playerId]}");
                
                FinanceHelper.RecalculateMonthlyIncome(state, playerId);
            }
        }
    }
}