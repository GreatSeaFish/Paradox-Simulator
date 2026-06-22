// res://View/WorldMapRender.cs
using Godot;
using System;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;

public partial class WorldMapRender : Node2D
{
    private TileMapLayer _groundLayer = null!;
    private TileMapLayer _surfaceLayer = null!;

    public override void _Ready()
    {
        // 1. 只获取属于自己强相关的图层节点
        _groundLayer = GetNode<TileMapLayer>("GroundLayer");
        _surfaceLayer = GetNode<TileMapLayer>("SurfaceLayer");

        if (_groundLayer == null)
        {
            GD.PrintErr("[MapRender] 错误: 未找到 GroundLayer 节点！");
            return;
        }

        _groundLayer.Clear();
        _surfaceLayer?.Clear();

        var mapData = CoreHost.MapConfig;
        if (mapData == null || mapData.Tiles.Count == 0)
        {
            GD.PrintErr("[MapRender] 错误: 核心层 MapData 未初始化或没有数据！");
            return;
        }

        GD.Print($"[MapRender] 正在从逻辑层提取数据并渲染底图，总共: {mapData.Tiles.Count} 个地块。");
        
        // 2. 纯粹绘制底图和地表物
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
    }
}