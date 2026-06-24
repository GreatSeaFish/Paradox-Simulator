using System.Linq;
using ParadoxSimulator.Simulation.State;
using ParadoxSimulator.Simulation.State.WorldModel;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;

namespace ParadoxSimulator.Simulation.Commands
{
    public class UnitMoveCommand : IGameCommand
    {
        public int UnitId { get; set; } // 【替换】：不再使用 SourceHex
        public HexCoord TargetHex { get; set; }

        public void Execute(WorldSimulationState state, int playerId)
        {
            // 1. 凭唯一的 ID 精准抓取这支部队，不再存在抓错人的情况！
            if (!state.DeployedUnits.TryGetValue(UnitId, out var targetUnit)) return;
            if (targetUnit.OwnerId != playerId) return; // 校验所有权

            // 2. 如果这支部队之前正在行军，拔除它旧的任务
            var oldTask = state.ActiveUnitMoves.Values.FirstOrDefault(m => m.UnitId == UnitId);
            if (oldTask != null)
            {
                state.ActiveUnitMoves.Remove(oldTask.TaskId);
            }

            // 3. 寻路计算 (起点直接使用这支部队当前挂载的确切坐标)
            var mapConfig = CoreHost.MapConfig;
            var path = HexPathfinder.FindPath(targetUnit.CurrentLocation, TargetHex, mapConfig, state, playerId);
            if (path == null || path.Count < 2) return;
            
            var waypoints = path.Skip(1).ToList();
            int firstStepCost = HexPathfinder.GetDynamicTerrainCost(waypoints[0], mapConfig, state, playerId);
            if (firstStepCost < 0) return;
            
            int totalDays = firstStepCost * 5; 

            // 4. 生成专属于这支部队的移动任务
            var task = new WorldSimulationState.UnitMoveTask
            {
                TaskId = state.NextMoveTaskId++,
                UnitId = targetUnit.UnitId,
                PlayerId = playerId,
                Waypoints = waypoints,
                TotalDaysForNextTile = totalDays,
                RemainingDaysForNextTile = totalDays
            };
            
            state.ActiveUnitMoves[task.TaskId] = task;
        }
    }
}