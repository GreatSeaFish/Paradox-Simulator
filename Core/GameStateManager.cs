using System;
using FixedMath.NET;
using Shared.Math;
using ParadoxSimulator.Core.WorldMapSystem;

namespace ParadoxSimulator.Core
{
    /// <summary>
    /// 定义游戏全局状态
    /// </summary>
    public enum GameState
    {
        Lobby,      // 未开始 / 在大厅中
        Playing,    // 游戏中
        Finished    // 游戏结束
    }

    /// <summary>
    /// 全局状态管理器
    /// 职责：维护当前游戏所处的状态，并负责在状态切换时初始化/清理相应的逻辑数据
    /// </summary>
    public class GameStateManager
    {
        public GameState CurrentState { get; private set; } = GameState.Lobby;

        // 状态切换事件，如果有 UI 需要监听状态变化，可以挂载这个事件
        public event Action<GameState>? OnStateChanged;

        /// <summary>
        /// 触发开局，初始化纯逻辑层数据
        /// </summary>
        public void StartGame()
        {
            if (CurrentState == GameState.Playing) return;

            CurrentState = GameState.Playing;
            ClientDebuger.LogHandler?.Invoke("[GameStateManager] 状态切换为: Playing，开始初始化游戏逻辑数据...");

            // ==========================================
            // 把原先 GlobalPlayerState.InitializeGame() 的逻辑移到这里
            // ==========================================
            GlobalPlayerState.PlayerPositions.Clear();
            foreach (var player in LocalClientInfo.LobbyPlayers)  
            {
                // 1. 获取分配给该槽位的六边形出生点
                HexCoord spawnHex = GlobalPlayerState.SpawnPoints[player.SlotId];
                
                // 2. 写入玩家初始逻辑坐标
                GlobalPlayerState.PlayerPositions[player.PlayerId] = new FixVector2((Fix64)spawnHex.X, (Fix64)spawnHex.Y);
                
                // 3. 将该出生点地块的归属权转移给该玩家
                CoreHost.MapData.SetTileOwner(spawnHex, player.PlayerId);
            }

            // 通知外部状态已改变
            OnStateChanged?.Invoke(CurrentState);
        }

        /// <summary>
        /// 结束游戏/返回大厅（预留接口）
        /// </summary>
        public void ReturnToLobby()
        {
            CurrentState = GameState.Lobby;
            OnStateChanged?.Invoke(CurrentState);
        }
    }
}