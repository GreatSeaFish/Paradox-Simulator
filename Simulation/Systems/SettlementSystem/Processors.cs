using System.Collections.Generic;
using System.Linq;
using ParadoxSimulator.Simulation.State;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;
using ParadoxSimulator.Simulation.State.WorldModel;
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
    
public class UnitMoveProcessor : ISettlementProcessor
    {
        public SettlementFrequency Frequency => SettlementFrequency.Daily;

        public void Execute(WorldSimulationState state)
        {
            var mapConfig = CoreHost.MapConfig;
            var completedTasks = new List<int>();

            foreach (var kvp in state.ActiveUnitMoves)
            {
                var task = kvp.Value;
                task.RemainingDaysForNextTile--;

                // ==========================================
                // 走到下一格的进度条满了，执行瞬移与占领！
                // ==========================================
                if (task.RemainingDaysForNextTile <= 0)
                {
                    HexCoord nextHex = task.Waypoints[0];
                    int cost = HexPathfinder.GetDynamicTerrainCost(nextHex, mapConfig, state, task.PlayerId);
                    if (cost < 0) 
                    {
                        completedTasks.Add(task.TaskId);
                        continue;
                    }

                    // 【关键点1】：在平移前，先查清楚这块地原本是谁的
                    int oldOwnerId = state.GetTileOwner(nextHex);

                    // 【核心平移】：直接修改实体的坐标属性
                    var unit = state.DeployedUnits[task.UnitId];
                    HexCoord oldLocation = unit.CurrentLocation;
                    unit.CurrentLocation = nextHex;

                    // ==========================================
                    // 【新增核心机制】：武力踩踏占领逻辑,如果是 -1 (无主之地)，则只路过，不占领！
                    // ==========================================
                    if (oldOwnerId != task.PlayerId && oldOwnerId != -1)
                    {
                        // 1. 易帜：强制将地块归属权改为当前部队的玩家
                        // (这里会自动触发 UI 事件重绘领土颜色)
                        state.SetTileOwner(nextHex, task.PlayerId);

                        // 2. 重新计算占领者的月度预期收入（由于领土增加，资金产出会变多）
                        FinanceHelper.RecalculateMonthlyIncome(state, task.PlayerId);

                        // 3. 如果是从别的玩家/敌人手里抢来的，必须削减受害者的收入，防止帧同步经济撕裂
                        if (oldOwnerId != -1)
                        {
                            FinanceHelper.RecalculateMonthlyIncome(state, oldOwnerId);
                            
                            // 4. (战术打断) 如果敌人正在这块地上造兵或搞事，直接物理没收！
                            state.ActiveUnitBuilds.Remove(nextHex);
                            state.ActiveColonizations.Remove(nextHex);
                        }
                        
                        ClientDebugger.LogHandler?.Invoke($"[战报] 玩家 {task.PlayerId} 的部队攻占了坐标 ({nextHex.X}, {nextHex.Y}, {nextHex.Z})！");
                    }
                    // ==========================================

                    // 通知 UI 这支特指的部队发生了位移
                    state.NotifyUnitStepped(task.UnitId, oldLocation, nextHex);
                    task.Waypoints.RemoveAt(0);

                    // 检查是否已经到达整条大路径的终点
                    if (task.Waypoints.Count == 0)
                    {
                        completedTasks.Add(task.TaskId);
                    }
                    else
                    {
                        // 如果还有下一格，无缝初始化下一格的独立读条任务
                        HexCoord nextNextHex = task.Waypoints[0];
                        int nextCost = HexPathfinder.GetDynamicTerrainCost(nextNextHex, mapConfig, state, task.PlayerId);
                        if (nextCost < 0)
                        {
                            completedTasks.Add(task.TaskId); // 下下格被堵，直接在当前格停下
                        }
                        else
                        {
                            task.TotalDaysForNextTile = nextCost * 5;
                            task.RemainingDaysForNextTile = task.TotalDaysForNextTile;
                        }
                    }
                }
            }

            // 清理已完成或被迫中断的行军
            foreach (var taskId in completedTasks)
            {
                state.ActiveUnitMoves.Remove(taskId);
            }
        }
    }
    
    
}