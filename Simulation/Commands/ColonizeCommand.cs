using ParadoxSimulator.Simulation.State;
using ParadoxSimulator.Simulation.Systems;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;

namespace ParadoxSimulator.Simulation.Commands
{
    public class ColonizeCommand : IGameCommand
    {
        public HexCoord TargetHex { get; set; }

        public void Execute(WorldSimulationState state, int playerId)
        {
            // 1. 安全防御：防止客户端发来不存在的越界地块
            if (!state.TileOwners.ContainsKey(TargetHex)) return;

            // 2. 规则校验 A：必须是无主之地
            if (state.TileOwners[TargetHex] != -1) return;

            // 3. 规则校验 B：不能重复殖民
            // 帧同步的绝对公平性：如果两个玩家在同一帧点击了同一块地，服务器按接收顺序打包，这里只认第一个！
            if (state.ActiveColonizations.ContainsKey(TargetHex)) return;

            // 4. 规则校验 C：必须与自己现有的领地相邻
            bool isAdjacent = false;
            var neighbors = HexUtility.GetAllNeighbors(TargetHex);
            foreach (var neighbor in neighbors)
            {
                if (state.TileOwners.TryGetValue(neighbor, out int ownerId) && ownerId == playerId)
                {
                    isAdjacent = true;
                    break;
                }
            }
            if (!isAdjacent) return;

            // 5. 校验全部通过，正式启动殖民任务！
            state.ActiveColonizations[TargetHex] = new WorldSimulationState.ColonizationTask
            {
                PlayerId = playerId,
                RemainingDays = 100 // 设定初始需要 100 天
            };

            // 6. 动态更新玩家的月度预期账单（新增 1 块钱的维护费赤字）
            if (state.PlayerMonthlyFundsChange.TryGetValue(playerId, out int currentChange))
            {
                state.SetMonthlyFundsChange(playerId, currentChange - 1);
            }
        }
    }
}