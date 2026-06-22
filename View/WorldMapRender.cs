using Godot;
using System;
using System.Collections.Generic;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;

public partial class WorldMapRender : Node2D
{
    // 渲染图层
    private TileMapLayer _groundLayer;
    private TileMapLayer _surfaceLayer;
    // 选中标记
    private Sprite2D _selecteMark;
    // ====== [新增] 框选框 UI 引用与控制变量 ======
    private ColorRect _selectionBox;
    private bool _isDragging = false;
    private Vector2 _dragStartPos = Vector2.Zero;
    // 判定为“拖拽”的最小像素位移阈值
    private const float DragThreshold = 5.0f;
    
    // [新增] 定义一个地块取消选中的事件，用来通知 UI 关闭面板
    public event Action OnTileDeselected;

    // [新增] 记录表现层当前选中的 Hex 坐标
    private HexCoord? _currentSelectedHex = null;
    
    // [新增] 地块点击事件，向外传递点击的 Hex 坐标和其数据
    public event Action<HexCoord, HexTileData> OnTileSelected;
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
        // [新增] 动态获取控制台 UI 下的框选矩形
        _selectionBox = GetParent().GetNode<ColorRect>("PlayerController/Hud/Root/SelectionBox");
        if (_selectionBox != null) _selectionBox.Visible = false;
        // [新增] 获取选中框节点
        _selecteMark = GetNode<Sprite2D>("SelectMark");
        if (_selecteMark != null) _selecteMark.Visible = false;
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

    public override void _UnhandledInput(InputEvent @event)
    {
        // ====== 1. 鼠标点击与拖拽状态转换 ======
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                // 鼠标按下：记录起始位置（此时属于屏幕视口坐标，对应 HUD 空间）
                _dragStartPos = GetViewport().GetMousePosition();
                _isDragging = true;
            }
            else if (_isDragging)
            {
                // 鼠标松开：状态结束
                _isDragging = false;
                if (_selectionBox != null) _selectionBox.Visible = false;

                Vector2 dragEndPos = GetViewport().GetMousePosition();
                float distance = _dragStartPos.DistanceTo(dragEndPos);

                if (distance < DragThreshold)
                {
                    // 【判定为单选点击动作】
                    HandleSingleClick(dragEndPos);
                }
                else
                {
                    // 【判定为框选拖拽动作】
                    HandleBoxSelection(_dragStartPos, dragEndPos);
                }
                GetViewport().SetInputAsHandled();
            }
        }

        // ====== 2. 鼠标移动时实时刷新框选框尺寸 ======
        if (@event is InputEventMouseMotion mm && _isDragging)
        {
            Vector2 currentPos = GetViewport().GetMousePosition();
            if (_dragStartPos.DistanceTo(currentPos) >= DragThreshold)
            {
                UpdateSelectionBox(_dragStartPos, currentPos);
            }
        }

        // ====== 3. 按 ESC 取消选中逻辑不变 ======
        if (@event is InputEventKey ek && ek.Pressed && ek.Keycode == Key.Escape)
        {
            if (_currentSelectedHex.HasValue)
            {
                ClearSelection();           
                OnTileDeselected?.Invoke();  
                GetViewport().SetInputAsHandled();
            }
        }
    }
    
    
    // [新增] 专门处理单选地块的方法
    private void HandleSingleClick(Vector2 screenPos)
    {
        // 从全局屏幕坐标转换回 TileMap 本地世界坐标
        Vector2 localMousePos = ToLocal(GetGlobalToLocalPosition(screenPos));
        Vector2I mapCell = _groundLayer.LocalToMap(localMousePos);
        var cubeCoords = MapRenderBridge.OffsetToCube(mapCell.X, mapCell.Y);
        var clickedHex = new HexCoord(cubeCoords.cubeX, cubeCoords.cubeY, cubeCoords.cubeZ);

        if (CoreHost.MapConfig.Tiles.TryGetValue(clickedHex, out var tileData))
        {
            if (_currentSelectedHex.HasValue && _currentSelectedHex.Value == clickedHex)
            {
                ClearSelection();          
                OnTileDeselected?.Invoke(); 
            }
            else
            {
                _currentSelectedHex = clickedHex;
                if (_selecteMark != null)
                {
                    _selecteMark.GlobalPosition = _groundLayer.MapToLocal(mapCell) + _groundLayer.GlobalPosition;
                    _selecteMark.Visible = true;
                }
                OnTileSelected?.Invoke(clickedHex, tileData);
            }
        }
    }

    // [新增] 专门处理范围框选的方法
    private void HandleBoxSelection(Vector2 start, Vector2 end)
    {
        // 计算出拖拽矩形在二维世界坐标系中的 AABB 包围盒
        Vector2 worldStart = GetGlobalToLocalPosition(start);
        Vector2 worldEnd = GetGlobalToLocalPosition(end);

        float minX = Mathf.Min(worldStart.X, worldEnd.X);
        float maxX = Mathf.Max(worldStart.X, worldEnd.X);
        float minY = Mathf.Min(worldStart.Y, worldEnd.Y);
        float maxY = Mathf.Max(worldStart.Y, worldEnd.Y);
        Rect2 selectionRect = new Rect2(minX, minY, maxX - minX, maxY - minY);

        List<HexCoord> boxedHexes = new List<HexCoord>();

        // 遍历当前的纯内存只读配置地块
        foreach (var kvp in CoreHost.MapConfig.Tiles)
        {
            var tile = kvp.Value;
            var offset = MapRenderBridge.CubeToOffset(tile.X, tile.Y, tile.Z);
            Vector2I cellCoords = new Vector2I(offset.offsetX, offset.offsetY);
            
            // 获取每一个网格在表现层世界中的像素中心点
            Vector2 tileWorldPos = _groundLayer.MapToLocal(cellCoords) + _groundLayer.GlobalPosition;

            // 检查这个点是否落在了玩家拖拽的方框里
            if (selectionRect.HasPoint(tileWorldPos))
            {
                boxedHexes.Add(kvp.Key);
            }
        }

    }

    // [新增] 动态绘制框选框 UI 尺寸
    private void UpdateSelectionBox(Vector2 start, Vector2 end)
    {
        if (_selectionBox == null) return;

        float x = Mathf.Min(start.X, end.X);
        float y = Mathf.Min(start.Y, end.Y);
        float width = Mathf.Abs(start.X - end.X);
        float height = Mathf.Abs(start.Y - end.Y);

        _selectionBox.Position = new Vector2(x, y);
        _selectionBox.Size = new Vector2(width, height);
        _selectionBox.Visible = true;
    }

    // 辅助转换方法：把视口屏幕坐标转为世界全局坐标
    private Vector2 GetGlobalToLocalPosition(Vector2 screenPos)
    {
        return GetGlobalMousePosition() - (GetViewport().GetMousePosition() - screenPos);
    }

    // 清除选中状态
    // [更新] 清除选中状态时，同时清空坐标记录
    public void ClearSelection()
    {
        _currentSelectedHex = null;
        if (_selecteMark != null) _selecteMark.Visible = false;
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