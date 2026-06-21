using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ParadoxSimulator.Core.WorldMapSystem;

public class MapLoader 
{

    public class MapExportData
    {
        public string ExportTime { get; set; }
        public List<HexTileData> Tiles { get; set; }
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
}