using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ParadoxSimulator.Simulation.State;
using ParadoxSimulator.Simulation.State.WorldModel;
namespace ParadoxSimulator.Simulation.Systems.WorldMapSystem;
public class MapLoader(MapConfig config, WorldSimulationState simulationState)
{

    public class MapExportData
    {
        public string ExportTime { get; set; }
        public List<HexTileData> Tiles { get; set; }
        
        public List<BorderData> Borders { get; set; } // 添加这一行
    }

    public MapExportData LoadMap(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"找不到地图数据文件: {filePath}");
        }

        try
        {
            string jsonString = File.ReadAllText(filePath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<MapExportData>(jsonString, options);
        }
        catch (Exception ex)
        {
            throw new Exception($"解析地图 JSON 失败: {ex.Message}", ex);
        }
    }
    
    /// <summary>
    /// 加载并解析地图数据
    /// </summary>
    public void LoadMapData(string jsonPath)
    {
        config.Tiles.Clear();
        config.Boundaries.Clear(); // 【新增】清空边界数据
        simulationState.TileOwners.Clear(); // 【新增】每次重新加载地图时，清空归属数据
        
        try
        {
            var mapExportData = LoadMap(jsonPath);
            foreach (var tile in mapExportData.Tiles)
            {
                var coord = new HexCoord(tile.X, tile.Y, tile.Z);
                config.Tiles[coord] = tile;
                
                // 【新增】初始化时，所有地块默认归属为 -1（中立）
                simulationState.TileOwners[coord] = -1; 
            }
            // 【新增】载入边界数据
            if (mapExportData.Borders != null)
            {
                foreach (var border in mapExportData.Borders)
                {
                    var coord = new HexCoord(border.X, border.Y, border.Z);
                    config.Boundaries[coord] = border;
                }
            }
            
            ClientDebugger.LogHandler?.Invoke($"[MapData] 成功加载并构建地图数据，共 {config.Tiles.Count} 个地块。");
        }
        catch (Exception ex)
        {
            ClientDebugger.LogHandler?.Invoke($"[MapData] 加载地图失败: {ex.Message}");
        }
    }
}