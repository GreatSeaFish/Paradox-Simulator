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
                // 走到下一格的进度条满了，准备执行瞬移！
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

                    // 【新增核心机制】：行军拦截！侦察目标地块是否有敌军
                    var enemyUnit = state.DeployedUnits.Values.FirstOrDefault(u => u.CurrentLocation == nextHex && u.OwnerId != task.PlayerId);
                    
                    if (enemyUnit != null)
                    {
                        // 发现敌军！爆发战斗！
                        var combat = new WorldSimulationState.CombatSession
                        {
                            CombatId = state.NextCombatId++,
                            AttackerUnitId = task.UnitId,
                            DefenderUnitId = enemyUnit.UnitId,
                            Location = nextHex,
                            StartDay = state.GameDays
                        };
                        
                        // 1. 将战斗加入全局活跃战斗队列
                        state.ActiveCombats[combat.CombatId] = combat;
                        
                        // 2. 触发开战事件 (UI 可以据此播放特效)
                        state.NotifyCombatStarted(combat);
                        
                        // 3. 中断攻击方的行军任务 (打完之前不能接着走)
                        completedTasks.Add(task.TaskId);
                        
                        // 4. (战术打断) 如果防守方刚好也在试图行军，把他的行军也打断，强迫他迎战！
                        var defenderTask = state.ActiveUnitMoves.Values.FirstOrDefault(m => m.UnitId == enemyUnit.UnitId);
                        if (defenderTask != null)
                        {
                            completedTasks.Add(defenderTask.TaskId);
                        }
                        
                        ClientDebugger.LogHandler?.Invoke($"[战报] 玩家 {task.PlayerId} 的部队(ID:{task.UnitId}) 在坐标 ({nextHex.X},{nextHex.Y},{nextHex.Z}) 遭遇敌军(ID:{enemyUnit.UnitId})，战斗爆发！");
                        
                        // 拦截成功，跳过后续的平移和占领逻辑！
                        continue; 
                    }

                    // ==========================================
                    // 下面是原本正常的平移与占领逻辑 (如果没有遇到敌人)
                    // ==========================================
                    int oldOwnerId = state.GetTileOwner(nextHex);
                    
                    // 【核心平移】：直接修改实体的坐标属性
                    var unit = state.DeployedUnits[task.UnitId];
                    HexCoord oldLocation = unit.CurrentLocation;
                    unit.CurrentLocation = nextHex;

                    // 武力踩踏占领逻辑
                    if (oldOwnerId != task.PlayerId && oldOwnerId != -1)
                    {
                        state.SetTileOwner(nextHex, task.PlayerId);
                        FinanceHelper.RecalculateMonthlyIncome(state, task.PlayerId);
                        
                        if (oldOwnerId != -1)
                        {
                            FinanceHelper.RecalculateMonthlyIncome(state, oldOwnerId);
                            state.ActiveUnitBuilds.Remove(nextHex);
                            state.ActiveColonizations.Remove(nextHex);
                        }
                    }

                    state.NotifyUnitStepped(task.UnitId, oldLocation, nextHex);
                    task.Waypoints.RemoveAt(0);

                    // 检查是否已经到达整条大路径的终点
                    if (task.Waypoints.Count == 0)
                    {
                        completedTasks.Add(task.TaskId);
                    }
                    else
                    {
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
    /// <summary>
    /// 每日战斗心跳处理器
    /// </summary>
    public class CombatProcessor : ISettlementProcessor
    {
        public SettlementFrequency Frequency => SettlementFrequency.Daily;

        public void Execute(WorldSimulationState state)
        {
            var completedCombats = new List<int>();

            foreach (var kvp in state.ActiveCombats)
            {
                var combat = kvp.Value;
                
                // 1. 获取参战双方实体
                bool hasAttacker = state.DeployedUnits.TryGetValue(combat.AttackerUnitId, out var attacker);
                bool hasDefender = state.DeployedUnits.TryGetValue(combat.DefenderUnitId, out var defender);

                // 安全防御：如果任何一方因为其他原因（如被代码强行移除）消失了，直接中止这场战斗
                if (!hasAttacker || !hasDefender)
                {
                    completedCombats.Add(combat.CombatId);
                    int winnerId = hasAttacker ? attacker.UnitId : (hasDefender ? defender.UnitId : -1);
                    state.NotifyCombatEnded(combat.CombatId, winnerId);
                    continue;
                }

                // 2. 【核心确定性】：初始化纯整数随机数发生器
                // 只要天数和战斗ID一致，任何电脑在这一帧算出来的骰子绝对一模一样！
                uint combatSeed = (uint)(state.GameDays + combat.CombatId);
                var dice = new Shared.Math.DeterministicRandom(combatSeed);

                // 3. 掷骰子 (0-9)
                int attackerRoll = dice.Range(0, 9);
                int defenderRoll = dice.Range(0, 9);

                // 4. 获取地形修正 (地形越崎岖，防守方越有利)
                // 平原 Cost=1，丘陵 Cost=2。我们将其减 1 作为攻击方的掷骰惩罚。
                int terrainCost = HexPathfinder.GetDynamicTerrainCost(combat.Location, CoreHost.MapConfig, state, defender.OwnerId);
                int terrainPenalty = System.Math.Max(0, terrainCost - 1); 

                // 5. 计算最终战术乘数 (不可小于0)
                int mA = System.Math.Max(0, attackerRoll - terrainPenalty);
                int mD = System.Math.Max(0, defenderRoll);

                // 6. 纯整数战损计算 (公式: 对方兵力 * 我的乘数 / 100)
                int attackerCasualties = (defender.Headcount * mD) / 100;
                int defenderCasualties = (attacker.Headcount * mA) / 100;

                // 士气打击 (规定：每损失 1 个士兵，掉 0.5 士气。纯整数表达即乘以 50 再除以 100)
                int attackerMoraleDmg = (attackerCasualties * 50) / 100;
                int defenderMoraleDmg = (defenderCasualties * 50) / 100;

                // 7. 施加伤害，严防属性越界成负数
                attacker.Headcount = System.Math.Max(0, attacker.Headcount - attackerCasualties);
                defender.Headcount = System.Math.Max(0, defender.Headcount - defenderCasualties);
                
                attacker.Morale = System.Math.Max(0, attacker.Morale - attackerMoraleDmg);
                defender.Morale = System.Math.Max(0, defender.Morale - defenderMoraleDmg);

                // 抛出更新事件，通知 UI 的血条和士气条闪烁扣减
                state.NotifyCombatUpdated(combat);

                // 8. 判定胜负 (兵力死光或士气归零即为战败)
                bool attackerBroken = attacker.Headcount <= 0 || attacker.Morale <= 0;
                bool defenderBroken = defender.Headcount <= 0 || defender.Morale <= 0;

                if (attackerBroken || defenderBroken)
                {
                    completedCombats.Add(combat.CombatId);
                    
                    int winnerUnitId = -1;
                    
                    if (attackerBroken && defenderBroken) 
                    {
                        // 同归于尽，直接双双抹杀
                        state.RemoveUnit(attacker.UnitId);
                        state.RemoveUnit(defender.UnitId);
                    }
                    else if (attackerBroken)
                    {
                        // 攻击方战败，抹杀攻击方，防守方惨胜
                        winnerUnitId = defender.UnitId;
                        state.RemoveUnit(attacker.UnitId);
                    }
                    else
                    {
                        // 防守方战败，抹杀防守方
                        // 策略妥协：原本攻击方的移动任务已被打断。为了避免寻路状态机的复杂回滚，
                        // 胜利的攻击方会停留在原本的发起地块。玩家需要手动再次下达移动指令进行占领。
                        winnerUnitId = attacker.UnitId;
                        state.RemoveUnit(defender.UnitId);
                    }

                    state.NotifyCombatEnded(combat.CombatId, winnerUnitId);
                    ClientDebugger.LogHandler?.Invoke($"[战报] 战斗 {combat.CombatId} 结束！胜利者 ID: {winnerUnitId}");
                }
            }

            // 9. 每日清道夫：把打完的战斗从字典里安全移除
            foreach (var combatId in completedCombats)
            {
                state.ActiveCombats.Remove(combatId);
            }
        }
    }


}

