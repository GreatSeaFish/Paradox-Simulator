using Godot;
using System;
using System.Collections.Generic;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;

public partial class WorldMapRender : Node2D
{
    // 渲染图层
    private TileMapLayer _groundLayer;
    private TileMapLayer _surfaceLayer;
    
    // [新增] 存放 8 个玩家领地图层的数组
    private TileMapLayer[] _territoryLayers = new TileMapLayer[8];

    // 预设颜色库 (与大厅保持一致)
    private readonly Color[] _colorValues = { 
        Colors.Red, Colors.Blue, Colors.Orange, Colors.Green, 
        Colors.Yellow, Colors.Purple, Colors.Pink, Colors.LightBlue 
    };

    public override void _Ready()
    {
        _groundLayer = GetNode<TileMapLayer>("GroundLayer");
        _surfaceLayer = GetNode<TileMapLayer>("SurfaceLayer");

        // [新增] 初始化 8 个领地 TileMapLayer
        Node territorialMarkLayers = GetNode("OwnershipFilter/TerritorialMarkLayers");
        for (int i = 0; i < 8; i++)
        {
            // 获取 player1 到 player8，对应数组索引 0 到 7 (即大厅里的 SlotId 0-7)
            _territoryLayers[i] = territorialMarkLayers.GetNode<TileMapLayer>($"Player{i + 1}");
            _territoryLayers[i].Clear();
        }

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

        GD.Print($"[MapRender] 正在从逻辑层提取数据并渲染，总共: {mapData.Tiles.Count} 个地块。");
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

    // [更新] 领地渲染逻辑：使用 Godot 的 Terrain 自动连接
    public void UpdateOwnershipVisuals()
    {
        var mapData = CoreHost.WorldSimulationState;
        if (mapData == null) return;

        // 1. 准备 8 个列表，用于对被占领的坐标进行按玩家分组
        var layerCells = new Dictionary<int, Godot.Collections.Array<Vector2I>>();
        for (int i = 0; i < 8; i++)
        {
            layerCells[i] = new Godot.Collections.Array<Vector2I>();
        }

        // 2. 遍历逻辑层的归属数据，将坐标转换为 Vector2I 并塞入对应玩家的列表中
        foreach (var kvp in mapData.TileOwners)
        {
            int ownerId = kvp.Value;
            if (ownerId != -1) 
            {
                // 通过 PlayerId 找到该玩家的大厅配置信息
                var playerInfo = CoreHost.LocalContext.LobbyPlayers.Find(p => p.PlayerId == ownerId);
                if (playerInfo != null)
                {
                    var offset = MapRenderBridge.CubeToOffset(kvp.Key.X, kvp.Key.Y, kvp.Key.Z);
                    // playerInfo.SlotId 正好是 0-7
                    layerCells[playerInfo.SlotId].Add(new Vector2I(offset.offsetX, offset.offsetY));
                }
            }
        }

        // 3. 批量绘制并激活自动地形连接
        for (int i = 0; i < 8; i++)
        {
            var targetLayer = _territoryLayers[i];
            if (targetLayer == null) continue;

            // 绘制前先清空这一层
            targetLayer.Clear();

            if (layerCells[i].Count > 0)
            {
                // 核心 API: 批量赋予地形，Godot 会自动计算九宫格/十六宫格的相连逻辑
                // 参数 1: terrain_set 的索引 (通常是 0)
                // 参数 2: 需要绘制的坐标数组
                // 参数 3: terrain 的索引 (通常是 0)
                // 参数 4: 是否忽略空的地形 (默认填 0 或 1 均可)
                targetLayer.SetCellsTerrainConnect(layerCells[i], 0, 0, false);

                // 找到在这个位置上的玩家，将图层整体染成他选的颜色
                var playerInfo = CoreHost.LocalContext.LobbyPlayers.Find(p => p.SlotId == i);
                if (playerInfo != null)
                {
                    targetLayer.Modulate = _colorValues[playerInfo.ColorId];
                }
            }
        }
    }
}