// res://View/WorldMapRender.cs
using Godot;
using System;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;

public partial class WorldMapRender : Node2D
{
    private TileMapLayer _groundLayer = null!;
    private TileMapLayer _surfaceLayer = null!;
    private TileMapLayer _riverLayer = null!; // 【新增】河流层

    public override void _Ready()
    {
        _groundLayer = GetNode<TileMapLayer>("GroundLayer");
        _surfaceLayer = GetNode<TileMapLayer>("SurfaceLayer");
        _riverLayer = GetNode<TileMapLayer>("RiverLayer"); // 【新增】获取RiverLayer节点

        if (_groundLayer == null)
        {
            GD.PrintErr("[MapRender] 错误: 未找到 GroundLayer 节点！");
            return;
        }

        _groundLayer.Clear();
        _surfaceLayer?.Clear();
        _riverLayer?.Clear(); // 【新增】初始化清空河流层

        var mapData = CoreHost.MapConfig;
        if (mapData == null || mapData.Tiles.Count == 0)
        {
            GD.PrintErr("[MapRender] 错误: 核心层 MapData 未初始化或没有数据！");
            return;
        }

        GD.Print($"[MapRender] 正在从逻辑层提取数据并渲染底图...");

        // 1. 绘制地块与地表
        foreach (var kvp in mapData.Tiles)
        {
            var tile = kvp.Value;
            var offset = MapRenderBridge.CubeToOffset(tile.X, tile.Y, tile.Z);
            Vector2I cellCoords = new Vector2I(offset.offsetX, offset.offsetY);

            int groundSourceId = MapRenderBridge.GetSourceIdByTerrainName(tile.GroundType);
            if (groundSourceId != -1)
            {
                _groundLayer.SetCell(cellCoords, groundSourceId, new Vector2I(0, 0));
            }

            if (_surfaceLayer != null && !string.IsNullOrEmpty(tile.SurfaceType) && tile.SurfaceType != "None")
            {
                int surfaceSourceId = MapRenderBridge.GetSourceIdByTerrainName(tile.SurfaceType);
                if (surfaceSourceId != -1)
                {
                    _surfaceLayer.SetCell(cellCoords, surfaceSourceId, new Vector2I(0, 0));
                }
            }
        }

        // 2. 【新增】绘制边界（河流等）
        if (_riverLayer != null && mapData.Boundaries.Count > 0)
        {
            foreach (var kvp in mapData.Boundaries)
            {
                var border = kvp.Value;
                
                // 仅渲染河流类型的边界
                if (border.BorderType == "River")
                {
                    var coord = kvp.Key;
                    
                    // 边界的偏移坐标直接使用CubeToOffset计算，它会完美对齐到你的 80x80 河流 TileMap 上
                    var offset = MapRenderBridge.CubeToOffset(coord.X, coord.Y, coord.Z);
                    Vector2I cellCoords = new Vector2I(offset.offsetX, offset.offsetY);

                    // 确定使用哪一张倾斜的河流图片
                    int sourceId = GetRiverSourceId(coord);
                    if (sourceId != -1)
                    {
                        _riverLayer.SetCell(cellCoords, sourceId, new Vector2I(0, 0));
                    }
                }
            }
        }
    }

    /// <summary>
    /// 解析方向获取河流贴图的 Source ID
    /// </summary>
    private int GetRiverSourceId(HexCoord border)
    {
        // 桥接解算出这条边界是由哪两个主地块共享的
        var (tileA, tileB) = HexBorderBridge.GetTilesFromBorder(border);
        
        // 计算地块之间的方向差值
        HexCoord dir = new HexCoord(tileB.X - tileA.X, tileB.Y - tileA.Y, tileB.Z - tileA.Z);

        // 匹配对应的 TileSet Source ID（基于你 TileSet_4kru3 的贴图顺序）：
        // 左 / 右 相邻，边界形态为垂直线 (|) -> RiverVertical.png -> Source ID: 2
        if (dir == new HexCoord(1, -1, 0) || dir == new HexCoord(-1, 1, 0)) 
            return 2;

        // 左下 / 右上 相邻，边界形态为右上倾斜 (/) -> RiverTopRight.png -> Source ID: 1
        if (dir == new HexCoord(1, 0, -1) || dir == new HexCoord(-1, 0, 1)) 
            return 1;

        // 左上 / 右下 相邻，边界形态为左上倾斜 (\) -> RiverTopLift.png -> Source ID: 0
        if (dir == new HexCoord(0, 1, -1) || dir == new HexCoord(0, -1, 1)) 
            return 0;

        return -1;
    }
}