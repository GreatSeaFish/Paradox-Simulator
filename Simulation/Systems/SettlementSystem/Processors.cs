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
                // 【新增】：有了新兵，月度维护费增加，立刻重算并刷新 UI
                FinanceHelper.RecalculateMonthlyIncome(state, ownerId);
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
            // ==========================================
            // 【新增】：每月全军士气恢复机制（恢复满值的 1/4 = 250点）
            // ==========================================
            foreach (var unit in state.DeployedUnits.Values)
            {
                if (unit.Headcount > 0)
                {
                    unit.Morale = System.Math.Min(1000, unit.Morale + 250); 
                }
            }
            
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

                if (task.RemainingDaysForNextTile <= 0)
                {
                    HexCoord nextHex = task.Waypoints[0];
                    int cost = HexPathfinder.GetDynamicTerrainCost(nextHex, mapConfig, state, task.PlayerId);
                    if (cost < 0) 
                    {
                        completedTasks.Add(task.TaskId);
                        continue;
                    }

                    // ==========================================
                    // 1. 允许跨入该地块 (无论有没有敌人)
                    // ==========================================
                    var unit = state.DeployedUnits[task.UnitId];
                    HexCoord oldLocation = unit.CurrentLocation;
                    unit.CurrentLocation = nextHex;
                    state.NotifyUnitStepped(task.UnitId, oldLocation, nextHex);

// ==========================================
                    // 【重构核心】：多单位实时战场动态切入机制
                    // ==========================================
                    var enemyUnit = state.DeployedUnits.Values.FirstOrDefault(u => u.CurrentLocation == nextHex && u.OwnerId != task.PlayerId);
                    if (enemyUnit != null)
                    {
                        // 侦察该格子是否已经存在正在进行的聚合战斗了
                        bool alreadyInCombat = state.ActiveCombats.Values.Any(c => c.Location == nextHex);
                        if (!alreadyInCombat)
                        {
                            // 如果是首战爆发，创建地块级聚合战场
                            var combat = new WorldSimulationState.CombatSession
                            {
                                CombatId = state.NextCombatId++,
                                Location = nextHex,
                                StartDay = state.GameDays
                            };
                            state.ActiveCombats[combat.CombatId] = combat;
                            state.NotifyCombatStarted(combat);
                        }

                        // 【核心】：无论是新战场还是已有战场，只要撞见敌军，立刻打断行军，投入到该聚合战斗中！
                        completedTasks.Add(task.TaskId);
                        
                        // 战术打断：如果被撞上的防守方刚好也想溜，强行把它也留下来迎战
                        var defenderTask = state.ActiveUnitMoves.Values.FirstOrDefault(m => m.UnitId == enemyUnit.UnitId);
                        if (defenderTask != null) completedTasks.Add(defenderTask.TaskId);
                        
                        ClientDebugger.LogHandler?.Invoke($"[动态增援] 玩家 {task.PlayerId} 的部队(ID:{task.UnitId}) 进入了坐标 ({nextHex.X},{nextHex.Y},{nextHex.Z}) 战场并实时加入战斗！");
                        continue; 
                    }
                    // ==========================================
                    // 3. 正常行军推进 (不再瞬间占领领土)
                    // ==========================================
                    task.Waypoints.RemoveAt(0);

                    if (task.Waypoints.Count == 0)
                    {
                        completedTasks.Add(task.TaskId);
                    }
                    else
                    {
                        HexCoord nextNextHex = task.Waypoints[0];
                        int nextCost = HexPathfinder.GetDynamicTerrainCost(nextNextHex, mapConfig, state, task.PlayerId);
                        if (nextCost < 0) completedTasks.Add(task.TaskId);
                        else
                        {
                            task.TotalDaysForNextTile = nextCost * 5;
                            task.RemainingDaysForNextTile = task.TotalDaysForNextTile;
                        }
                    }
                }
            }

            foreach (var taskId in completedTasks) state.ActiveUnitMoves.Remove(taskId);
        }
    }


/// <summary>
    /// 集团军多对多每日战斗心跳处理器
    /// </summary>
    public class CombatProcessor : ISettlementProcessor
    {
        public SettlementFrequency Frequency => SettlementFrequency.Daily;

        public void Execute(WorldSimulationState state)
        {
            var completedCombats = new List<int>();
            // 用于临时存储已结束战场的数据，方便在移除后进行表现层通知
            var finishedCombatData = new List<(int id, HexCoord loc, int winner)>();

            foreach (var kvp in state.ActiveCombats)
            {
                var combat = kvp.Value;
                HexCoord hex = combat.Location;

                // 1. 搜集当前地块上所有驻扎的部队
                var unitsHere = state.DeployedUnits.Values.Where(u => u.CurrentLocation == hex).ToList();
                if (unitsHere.Count < 2)
                {
                    completedCombats.Add(combat.CombatId);
                    finishedCombatData.Add((combat.CombatId, hex, -1));
                    continue;
                }

                // 2. 动态划分为两大对立阵营
                int factionA_Id = unitsHere[0].OwnerId;
                var factionA_Units = unitsHere.Where(u => u.OwnerId == factionA_Id).ToList();
                var factionB_Units = unitsHere.Where(u => u.OwnerId != factionA_Id).ToList();

                // 如果某一侧由于之前的战斗死光/撤退了，战斗终止
                if (factionA_Units.Count == 0 || factionB_Units.Count == 0)
                {
                    completedCombats.Add(combat.CombatId);
                    int winnerId = factionA_Units.Count > 0 ? factionA_Units[0].UnitId : factionB_Units[0].UnitId;
                    finishedCombatData.Add((combat.CombatId, hex, winnerId));
                    continue;
                }

                // 3. 计算聚合战场各方的总兵力
                int totalHeadcountA = factionA_Units.Sum(u => u.Headcount);
                int totalHeadcountB = factionB_Units.Sum(u => u.Headcount);

                // 4. 重构版：千分比稳定随机发生器 (降低方差，提高保底)
                uint combatSeed = (uint)(state.GameDays + combat.CombatId);
                var dice = new Shared.Math.DeterministicRandom(combatSeed);
                
                // 掷出 20 到 45 的值，代表每天 2.0% ~ 4.5% 的基础战损倍率
                int rollA = dice.Range(20, 46);
                int rollB = dice.Range(20, 46);

                // 地形政治阻挡修正（放大地形的防御优势）
                int terrainCost = HexPathfinder.GetDynamicTerrainCost(hex, CoreHost.MapConfig, state, factionB_Units[0].OwnerId);
                int terrainPenalty = System.Math.Max(0, (terrainCost - 1) * 5);

                // 保底机制：进攻方哪怕地形再差，也至少能造成 1.0% 的基础伤害
                int mA = System.Math.Max(10, rollA - terrainPenalty);
                int mD = System.Math.Max(10, rollB);

                // 5. 集团军交叉总战损计算 (千分比)
                int totalCasualtiesToA = (totalHeadcountB * mD) / 1000;
                int totalCasualtiesToB = (totalHeadcountA * mA) / 1000;

                // 强制保底伤害：防止被无伤全歼
                totalCasualtiesToA += (totalHeadcountB > 0 ? 3 : 0);
                totalCasualtiesToB += (totalHeadcountA > 0 ? 3 : 0);

                // 6. 伤害全军分摊机制
                DistributeGroupDamage(factionA_Units, totalCasualtiesToA);
                DistributeGroupDamage(factionB_Units, totalCasualtiesToB);

                // 刷新 UI 的血条和士气条
                state.NotifyCombatUpdated(combat);

                // 7. 只清理物理上死绝的炮灰，绝不提前删除士气崩溃的单位
                var deadA = factionA_Units.Where(u => u.Headcount <= 0).ToList();
                var deadB = factionB_Units.Where(u => u.Headcount <= 0).ToList();
                
                foreach (var u in deadA) state.RemoveUnit(u.UnitId);
                foreach (var u in deadB) state.RemoveUnit(u.UnitId);

                // 重新获取存活（包含士气崩溃但肉体还活着）的单位列表
                var aliveA = factionA_Units.Where(u => u.Headcount > 0).ToList();
                var aliveB = factionB_Units.Where(u => u.Headcount > 0).ToList();

                // 8. 判定大战场胜负（只要一方存活者的士气全部 <= 0，即判定该方大败）
                bool sideAWiped = aliveA.Count == 0 || aliveA.All(u => u.Morale <= 0);
                bool sideBWiped = aliveB.Count == 0 || aliveB.All(u => u.Morale <= 0);

                if (sideAWiped || sideBWiped)
                {
                    completedCombats.Add(combat.CombatId);
                    
                    // 找出战败的一方和获胜的一方
                    var loserUnits = sideAWiped ? aliveA : aliveB;
                    var winnerUnits = sideAWiped ? aliveB : aliveA;
                    
                    // 如果双方同归于尽，直接双灭，跳过撤退
                    if (loserUnits.Count == 0 && winnerUnits.Count == 0) continue; 

                    int winnerPlayerId = winnerUnits.Count > 0 ? winnerUnits.First().OwnerId : -1;
                    int loserPlayerId = loserUnits.Count > 0 ? loserUnits.First().OwnerId : -1;

                    int totalWinnerHeadcount = winnerUnits.Sum(u => u.Headcount);
                    int totalLoserHeadcount = loserUnits.Sum(u => u.Headcount);

                    // 9. 执行撤退 vs 溃灭 断言
                    if (totalLoserHeadcount < (totalWinnerHeadcount / 2))
                    {
                        // 兵力悬殊且士气崩溃，无情抹杀全歼
                        ClientDebugger.LogHandler?.Invoke($"[战报] 阵营 {loserPlayerId} 兵力悬殊且士气崩溃，全军覆没！");
                        foreach (var u in loserUnits) state.RemoveUnit(u.UnitId);
                    }
                    else
                    {
                        // 兵力尚存但士气崩溃，执行战略撤退寻路
                        HexCoord retreatDestination = FindSafeRetreatTile(hex, loserPlayerId, state);

                        if (retreatDestination == default(HexCoord))
                        {
                            ClientDebugger.LogHandler?.Invoke($"[战报] 阵营 {loserPlayerId} 无路可逃，被迫就地解散全灭！");
                            foreach (var u in loserUnits) state.RemoveUnit(u.UnitId);
                        }
                        else
                        {
                            ClientDebugger.LogHandler?.Invoke($"[战略撤退] 阵营 {loserPlayerId} 残兵败退至坐标 ({retreatDestination.X},{retreatDestination.Y},{retreatDestination.Z})。");
                            
                            foreach (var u in loserUnits)
                            {
                                HexCoord oldLoc = u.CurrentLocation;
                                // 强行拔除其原有的行军任务
                                var oldTask = state.ActiveUnitMoves.Values.FirstOrDefault(m => m.UnitId == u.UnitId);
                                if (oldTask != null) state.ActiveUnitMoves.Remove(oldTask.TaskId);

                                // 瞬间物理位移到撤退目的地
                                u.CurrentLocation = retreatDestination;
                                u.Morale = 100; // 撤退成功，给10%保底士气
                                
                                state.NotifyUnitStepped(u.UnitId, oldLoc, retreatDestination);
                            }
                        }
                    }

                    int finalWinnerId = winnerUnits.FirstOrDefault(u => u.Headcount > 0)?.UnitId ?? -1;
                    finishedCombatData.Add((combat.CombatId, hex, finalWinnerId));
                }
            }

            // ==========================================
            // 收尾阶段：严格先移除字典，后广播事件，最后刷新财务
            // ==========================================
            foreach (var combatId in completedCombats) 
            {
                state.ActiveCombats.Remove(combatId);
            }

            foreach (var data in finishedCombatData)
            {
                state.NotifyCombatEnded(data.id, data.loc, data.winner);
                ClientDebugger.LogHandler?.Invoke($"[战报] 聚合战场 ({data.loc.X},{data.loc.Y},{data.loc.Z}) 战事平息。");
                
                // 战斗结束后，为留下来的幸存者（或者被全歼的空军）刷新财务账单
                var remainingUnits = state.DeployedUnits.Values.Where(u => u.CurrentLocation == data.loc).ToList();
                if(remainingUnits.Count > 0)
                {
                    FinanceHelper.RecalculateMonthlyIncome(state, remainingUnits[0].OwnerId);
                }
            }
        }

        /// <summary>
        /// 集团军伤害按兵力权重等比分摊的核心算法 (优化士气崩溃权重)
        /// </summary>
        private void DistributeGroupDamage(List<WorldSimulationState.MilitaryUnit> factionUnits, int totalCasualties)
        {
            if (factionUnits.Count == 0 || totalCasualties <= 0) return;
            int currentTotalHead = factionUnits.Sum(u => u.Headcount);
            if (currentTotalHead <= 0) return;

            foreach (var u in factionUnits)
            {
                if (u.Headcount <= 0) continue;
                
                // 按该单位占本集团军的兵力比重分摊战损
                int allocatedDmg = (int)((long)totalCasualties * u.Headcount / currentTotalHead);
                
                // 计算瞬间伤亡比例 (用千分比表示，死得越惨比例越大)
                int casualtyRate = (allocatedDmg * 1000) / u.Headcount;
                
                u.Headcount = System.Math.Max(0, u.Headcount - allocatedDmg);

                // 士气暴跌公式：阵亡惩罚 + 惊骇比例惩罚 + 每日基础疲劳
                int moraleDmg = (allocatedDmg * 3) + (casualtyRate * 2) + 15;
                u.Morale = System.Math.Max(0, u.Morale - moraleDmg);
            }
        }

        /// <summary>
        /// 撤退智能路由解算器
        /// </summary>
        private HexCoord FindSafeRetreatTile(HexCoord combatLoc, int loserPlayerId, WorldSimulationState state)
        {
            // 1. 搜集全图所有属于该败军玩家的有效领土
            var myTerritories = state.TileOwners.Where(kvp => kvp.Value == loserPlayerId).Select(kvp => kvp.Key).ToList();
            if (myTerritories.Count == 0) return default;

            var mapConfig = CoreHost.MapConfig;
            var viableTiles = new List<HexCoord>();

            // 2. 连通性测试：只有能够寻路得到的土地才是可以撤回去的
            foreach (var tile in myTerritories)
            {
                var path = HexPathfinder.FindPath(combatLoc, tile, mapConfig, state, loserPlayerId);
                if (path != null && path.Count > 0)
                {
                    viableTiles.Add(tile);
                }
            }

            if (viableTiles.Count == 0) return default;

            // 3. 危险系数打分排序（优先找三环内完全没有敌人的领土）
            var bestTiles = new List<HexCoord>();
            var backupTiles = new List<HexCoord>();

            foreach (var tile in viableTiles)
            {
                // 扫描该地块周围 3 环内所有的格子
                var threeRingHexes = HexUtility.GetHexesInRange(tile, 3);
                
                // 检查这群格子里是否混进了敌人的驻军
                bool hasEnemyNearby = state.DeployedUnits.Values.Any(u => u.OwnerId != loserPlayerId && threeRingHexes.Contains(u.CurrentLocation));

                if (!hasEnemyNearby) bestTiles.Add(tile);
                else backupTiles.Add(tile);
            }

            // 4. 给出评估答案：有完美的选完美（离战场最近的完美领土），没完美的选普通连通领土
            if (bestTiles.Count > 0)
            {
                return bestTiles.OrderBy(t => HexCoord.Distance(combatLoc, t)).First();
            }
            
            return backupTiles.OrderBy(t => HexCoord.Distance(combatLoc, t)).First();
        }
    }



/// <summary>
    /// 领土占领每日结算处理器
    /// </summary>
    public class OccupationProcessor : ISettlementProcessor
    {
        public SettlementFrequency Frequency => SettlementFrequency.Daily;
        
        public void Execute(WorldSimulationState state)
        {
            var coordsToOccupy = state.DeployedUnits.Values.Select(u => u.CurrentLocation).Distinct().ToList();
            var invalidOccupations = state.ActiveOccupations.Keys.Where(k => !coordsToOccupy.Contains(k)).ToList();
            
            foreach(var invalidCoord in invalidOccupations) state.ActiveOccupations.Remove(invalidCoord);

            foreach (var coord in coordsToOccupy)
            {
                if (state.ActiveCombats.Values.Any(c => c.Location == coord)) continue;

                var unitsHere = state.DeployedUnits.Values.Where(u => u.CurrentLocation == coord).ToList();
                int currentOwner = state.GetTileOwner(coord);

                // ==========================================
                // 【核心修复 Bug 2】：禁止军队直接肉身踩踏占领中立白地
                // ==========================================
                if (currentOwner == -1)
                {
                    // 如果原本存在异常的占领任务（比如白地），将其移除，直接跳过，白地必须走手动 ColonizeCommand
                    state.ActiveOccupations.Remove(coord);
                    continue;
                }

                int occupyingPlayer = unitsHere[0].OwnerId;

                // 如果该地块全是同一个玩家的部队，且地块不属于他（此处暗含必须是敌对玩家的有效领土所有权）
                if (unitsHere.All(u => u.OwnerId == occupyingPlayer) && currentOwner != occupyingPlayer)
                {
                    if (!state.ActiveOccupations.ContainsKey(coord))
                    {
                        state.ActiveOccupations[coord] = new WorldSimulationState.OccupationTask { OccupyingPlayerId = occupyingPlayer };
                    }

                    var task = state.ActiveOccupations[coord];
                    task.AccumulatedDays++;

                    if (task.AccumulatedDays >= 30)
                    {
                        state.SetTileOwner(coord, occupyingPlayer);
                        state.ActiveOccupations.Remove(coord);
                        FinanceHelper.RecalculateMonthlyIncome(state, occupyingPlayer);
                        FinanceHelper.RecalculateMonthlyIncome(state, currentOwner); // 敌方扣除维护/产出
                        
                        ClientDebugger.LogHandler?.Invoke($"[占领] 玩家 {occupyingPlayer} 通过30天坚守，成功剥夺了玩家 {currentOwner} 的领土权 ({coord.X},{coord.Y},{coord.Z})！");
                    }
                }
                else
                {
                    state.ActiveOccupations.Remove(coord); 
                }
            }
        }
    }
    
}

