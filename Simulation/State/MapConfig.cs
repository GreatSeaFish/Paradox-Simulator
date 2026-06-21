using System.Collections.Generic;
using ParadoxSimulator.Core.WorldMapSystem;

namespace ParadoxSimulator.Core.GameData;

/// <summary>
/// 静态地图结构与地貌缓存 (常驻内存，只读)
/// </summary>
public class MapConfig
{
    // 静态地形数据（来自 JSON 配置解析）
    public Dictionary<HexCoord, HexTileData> Tiles { get; } = new Dictionary<HexCoord, HexTileData>();
}