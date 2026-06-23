using FixedMath.NET;
using Shared.Math;
using ParadoxSimulator.Simulation.State;
using ParadoxSimulator.Simulation.Systems.SettlementSystem;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;
using ParadoxSimulator.Simulation.State.WorldModel;
namespace ParadoxSimulator.Simulation.Systems
{


    /// <summary>
    /// 全局状态管理器
    /// 职责：维护当前游戏所处的状态，并负责在状态切换时初始化/清理相应的逻辑数据
    /// </summary>
    public class GameStateSystem(GameState gameState, WorldSimulationState simulationState, LocalContext localContext, SettlementSystem.SettlementSystem settlementSystem)
    {

        /// <summary>
        /// 触发开局，初始化纯逻辑层数据
        /// </summary>
        public void StartGame()
        {
            if (gameState.CurrentState == GamePhase.Playing) return;

            gameState.CurrentState = GamePhase.Playing;
            ClientDebugger.LogHandler?.Invoke("[GameStateSystem] 状态切换为: Playing，开始初始化游戏逻辑数据...");

            simulationState.PlayerPositions.Clear();
            simulationState.PlayerFunds.Clear(); // 【新增】清空历史资金
            simulationState.ActiveUnitBuilds.Clear();
            simulationState.DeployedUnits.Clear();
            
            foreach (var player in localContext.LobbyPlayers)  
            {
                int playerId = player.PlayerId;
                // 1. 获取分配给该槽位的六边形出生点
                HexCoord spawnHex = gameState.SpawnPoints[player.SlotId];
                
                // 2. 写入玩家初始逻辑坐标
                simulationState.PlayerPositions[player.PlayerId] = new FixVector2((Fix64)spawnHex.X, (Fix64)spawnHex.Y);
                
                // 3. 将该出生点地块的归属权转移给该玩家
                simulationState.SetTileOwner(spawnHex, player.PlayerId);
                
                // 为每位参与的玩家初始化资金仓库
                // CoreHost.SettlementSystem.RecalculateMonthlyIncome(playerId);
                // 1. 为每位参与的玩家初始化资金仓库
                simulationState.PlayerFunds[player.PlayerId] = 0;
                FinanceHelper.RecalculateMonthlyIncome(simulationState, player.PlayerId);
                simulationState.NotifyFundsChanged(player.PlayerId);
            }

        }
    }
}