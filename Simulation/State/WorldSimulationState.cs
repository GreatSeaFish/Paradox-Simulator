using System;
using System.Collections.Generic;
using Shared.Math;
using ParadoxSimulator.Core.WorldMapSystem;

namespace ParadoxSimulator.Core.GameData;

/// <summary>
/// 核心帧同步状态仓库 (必须保证多端绝对一致)
/// </summary>
public class WorldSimulationState
{
    // ================== 实体 ==================
    // 全房间所有玩家的最新逻辑帧坐标
    public Dictionary<int, FixVector2> PlayerPositions { get; set;} = new Dictionary<int, FixVector2>();

    // 地块动态运行时的归属数据 (Key: 坐标, Value: 玩家ID，-1 表示中立)
    public Dictionary<HexCoord, int> TileOwners { get; set;} = new Dictionary<HexCoord, int>();

    // ================== 时钟 ==================
    // 累计经历的逻辑帧数
    public int LocalTickCount { get; set; } = 0;
    
    // 游戏经过的天数
    public int GameDays { get; set; } = 0;
    
    // 模拟现实的日历字段 (默认从 1年4月1日 开始)
    public DateTime CurrentDate { get; set; } = new DateTime(1, 4, 1);
    
        
    /// <summary>
    /// 直接获取当前确定的确定性逻辑位置，不再进行插值计算
    /// </summary>
    public void GetLogicalPositions(Dictionary<int, FixVector2> outPos)
    {
        foreach (var kvp in PlayerPositions) 
        {
            outPos[kvp.Key] = kvp.Value;
        }
    }
    
    // ==========================================
    // =====          数据管理方法           =====
    // ==========================================
    
    /// <summary>
    /// 获取指定地块的归属玩家ID
    /// </summary>
    /// <returns>玩家ID，返回 -1 表示中立或无效地块</returns>
    public int GetTileOwner(HexCoord coord)
    {
        if (TileOwners.TryGetValue(coord, out int ownerId))
        {
            return ownerId;
        }
        return -1; 
    }

    /// <summary>
    /// 设置/更改指定地块的归属权
    /// </summary>
    /// <param name="coord">目标六边形坐标</param>
    /// <param name="playerId">新的玩家ID（传入 -1 可使其恢复中立）</param>
    public void SetTileOwner(HexCoord coord, int playerId)
    {
        if (TileOwners.ContainsKey(coord))
        {
            TileOwners[coord] = playerId;
            
            // TODO: 未来如果需要，可以在这里触发一个事件 (Event)
            // 通知表现层 (WorldMapRender) 刷新这个地块的边框颜色或网格颜色
        }
    }
}