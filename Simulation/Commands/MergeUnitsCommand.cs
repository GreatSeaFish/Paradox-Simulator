using System.Linq;
using ParadoxSimulator.Simulation.State.WorldModel;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;
using ParadoxSimulator.Simulation.Systems.SettlementSystem;

namespace ParadoxSimulator.Simulation.Commands
{
    public class MergeUnitsCommand : IGameCommand
    {
        public HexCoord TargetHex { get; set; }

        public void Execute(WorldSimulationState state, int playerId)
        {
            // 1. 安全防御：如果这一格正在发生战斗，严禁执行拆分合并，直接驳回操作
            bool isCombatActive = state.ActiveCombats.Values.Any(c => c.Location == TargetHex);
            if (isCombatActive) return;

            // 2. 筛选出当前格子中纯属该玩家的所有存活军事单位
            var myUnitsHere = state.DeployedUnits.Values
                .Where(u => u.CurrentLocation == TargetHex && u.OwnerId == playerId && u.Headcount > 0)
                .ToList();

            // 如果单位少于两个，不需要合并
            if (myUnitsHere.Count < 2) return;

            // 3. 确定性收集：统计总人数，并加权计算平均士气（保证多端浮点一致性的纯整数计算）
            long totalHeadcount = 0;
            long totalMoralePoints = 0;

            foreach (var unit in myUnitsHere)
            {
                totalHeadcount += unit.Headcount;
                totalMoralePoints += (long)unit.Morale * unit.Headcount; // 士气乘以人数做权重累加
            }

            if (totalHeadcount <= 0) return;

            // 计算融合后的加权平均士气 (如果总人数>0，保底不为0)
            int blendedMorale = (int)(totalMoralePoints / totalHeadcount);

            // 4. 正式物理抹杀：清理掉该格子上的所有旧行军任务和旧物理实体
            foreach (var unit in myUnitsHere)
            {
                // 拔除它们可能存在的旧行军任务（以防有些残兵本来在走路）
                var oldTask = state.ActiveUnitMoves.Values.FirstOrDefault(m => m.UnitId == unit.UnitId);
                if (oldTask != null)
                {
                    state.ActiveUnitMoves.Remove(oldTask.TaskId);
                }

                // 从全局仓库中移除实体
                state.RemoveUnit(unit.UnitId);
            }

            // 5. 确定性重构：按满编（1000人）规则向下拆分分配
            int maxCapacity = 1000;
            long remainingPeople = totalHeadcount;

            while (remainingPeople > 0)
            {
                // A. 计算当前新单位应该塞多少人
                int newHeadcount = (int)System.Math.Min(maxCapacity, remainingPeople);
                remainingPeople -= newHeadcount;

                // B. 直接调用底层的 SpawnUnit，让它安全、原子化地去自增 NextUnitId 并抛出 UI 刷新事件 
                state.SpawnUnit(TargetHex, playerId, newHeadcount);

                // C. 【关键修正】：由于 SpawnUnit 内部使用的是自增后的 ID ，新诞生的单位 ID 恰好是 state.NextUnitId - 1
                int latestUnitId = state.NextUnitId - 1;

                // D. 强行将这个刚刚诞生的单位的士气，修正为我们计算出的融合加权平均士气
                if (state.DeployedUnits.ContainsKey(latestUnitId))
                {
                    state.DeployedUnits[latestUnitId].Morale = blendedMorale;
                }
            }

            // 6. 核心更新：因为编队数量改变，立刻重刷该玩家的月度预期收支账单
            FinanceHelper.RecalculateMonthlyIncome(state, playerId);
        }
    }
}