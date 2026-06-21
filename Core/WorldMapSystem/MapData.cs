using System;
using System.Collections.Generic;

namespace ParadoxSimulator.Core.WorldMapSystem;

/// <summary>
/// 全局地图数据容器
/// 职责：在游戏启动时加载并常驻内存，供渲染层、寻路层等系统统一读取
/// </summary>
public class MapData
{
    // 静态地形数据（来自 JSON 配置）
    public Dictionary<HexCoord, HexTileData> Tiles { get; private set; } = new Dictionary<HexCoord, HexTileData>();

    // 【新增】动态运行时的地块归属数据 (Key: 坐标, Value: 玩家ID，-1 表示中立)
    public Dictionary<HexCoord, int> TileOwners { get; private set; } = new Dictionary<HexCoord, int>();

    /// <summary>
    /// 加载并解析地图数据
    /// </summary>
    public void LoadMapData(string jsonPath)
    {
        Tiles.Clear();
        TileOwners.Clear(); // 【新增】每次重新加载地图时，清空归属数据
        
        MapLoader loader = new MapLoader();
        
        try
        {
            var mapExportData = loader.LoadMap(jsonPath);
            foreach (var tile in mapExportData.Tiles)
            {
                var coord = new HexCoord(tile.X, tile.Y, tile.Z);
                Tiles[coord] = tile;
                
                // 【新增】初始化时，所有地块默认归属为 -1（中立）
                TileOwners[coord] = -1; 
            }
            ClientDebuger.LogHandler?.Invoke($"[MapData] 成功加载并构建地图数据，共 {Tiles.Count} 个地块。");
        }
        catch (Exception ex)
        {
            ClientDebuger.LogHandler?.Invoke($"[MapData] 加载地图失败: {ex.Message}");
        }
    }

    // ==========================================
    // ===== 【新增】归属权管理辅助方法 =====
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