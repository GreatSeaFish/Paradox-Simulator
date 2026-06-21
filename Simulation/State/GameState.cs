using System;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;

namespace ParadoxSimulator.Simulation.State;

public enum GamePhase
{
    Lobby,      // 未开始 / 在大厅中
    Playing,    // 游戏中
    Finished    // 游戏结束
}
/// <summary>
/// 游戏宏观进程状态
/// </summary>
public class GameState
{
    // 预设 1-8 号位置的出生点坐标
    public HexCoord[] SpawnPoints = new HexCoord[]
    {
        new HexCoord(0, 0, 0),      // Slot 0 (位置1)
        new HexCoord(3, -3, 0),     // Slot 1 (位置2)
        new HexCoord(-3, 3, 0),     // Slot 2 (位置3)
        new HexCoord(3, 0, -3),     // Slot 3 (位置4)
        new HexCoord(-3, 0, 3),     // Slot 4 (位置5)
        new HexCoord(0, 3, -3),     // Slot 5 (位置6)
        new HexCoord(0, -3, 3),     // Slot 6 (位置7)
        new HexCoord(6, -3, -3)     // Slot 7 (位置8)
    };
    
    public GamePhase CurrentState { get; set; } = GamePhase.Lobby;
    
    // 网络层/逻辑层是否已经正式开局的标记
    public bool IsGameStarted { get; set; } = false; 
    
    
    
    // 状态切换事件，如果有 UI 需要监听状态变化，可以挂载这个事件
    public event Action<GamePhase>? OnStateChanged;
}