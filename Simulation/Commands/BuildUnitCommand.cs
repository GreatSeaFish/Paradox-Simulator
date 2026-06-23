using ParadoxSimulator.Simulation.State;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;

namespace ParadoxSimulator.Simulation.Commands
{
    public class BuildUnitCommand : IGameCommand
    {
        public HexCoord TargetHex { get; set; }

        public void Execute(WorldSimulationState state, int playerId)
        {
            // 1. 安全防御：防止客户端发来不存在的越界地块
            if (!state.TileOwners.ContainsKey(TargetHex)) return;

            // 2. 规则校验 A：必须是该玩家自己的领地
            if (state.TileOwners[TargetHex] != playerId) return;

            // 3. 规则校验 B：该地块不能已经在造兵了 (防止帧同步连点并发)
            if (state.ActiveUnitBuilds.ContainsKey(TargetHex)) return;
            
            // 4. 规则校验 C：该地块上不能已经有部署好的部队 (假设一格只能有一支部队)
            if (state.DeployedUnits.ContainsKey(TargetHex)) return;

            // 5. 规则校验 D：检查资金是否足够 (造价 10 块钱)
            if (!state.PlayerFunds.TryGetValue(playerId, out int currentFunds) || currentFunds < 10) return;

            // 6. 校验全部通过，正式启动造兵任务！
            // 扣除 10 块钱 (调用此方法会自动触发 UI 的 Amount 变化)
            state.AddFundsRealtime(playerId, -10);

            // 将任务加入活跃队列
            state.ActiveUnitBuilds[TargetHex] = new WorldSimulationState.UnitBuildTask
            {
                PlayerId = playerId,
                RemainingDays = 30 // 设定需要 30 天
            };
        }
    }
}