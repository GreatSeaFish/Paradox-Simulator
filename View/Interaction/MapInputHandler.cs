// res://View/Interaction/MapInputHandler.cs
using Godot;
using System;
using System.Collections.Generic;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;

public partial class MapInputHandler : Node2D
{
    public event Action<Rect2>? OnBoxSelectedRect;
    private TileMapLayer _groundLayer = null!;
    private Sprite2D _selectMark = null!;
    private ColorRect _selectionBox = null!;

    // 对外暴露的交互事件 [cite: 603, 605]
    public event Action<HexCoord, HexTileData>? OnTileSelected;
    public event Action? OnTileDeselected;
// 【新增】：右键点击事件
    public event Action<HexCoord>? OnRightClicked;
    // 【新增】：框选完成事件
    public event Action<List<HexCoord>>? OnBoxSelectedHexes;
    private bool _isDragging = false;
    private Vector2 _dragStartGlobalPos = Vector2.Zero; // 【新增】：这是真实世界物理坐标（框兵用的）
    private Vector2 _dragStartPos = Vector2.Zero;
    private const float DragThreshold = 5.0f; // [cite: 602]
    private HexCoord? _currentSelectedHex = null; // [cite: 604]

    public void Init(TileMapLayer groundLayer, Sprite2D selectMark, ColorRect selectionBox)
    {
        _groundLayer = groundLayer;
        _selectMark = selectMark;
        _selectionBox = selectionBox;
    }
    public HexCoord GetCurrentSelectedHex() => _currentSelectedHex ?? default;
    public override void _UnhandledInput(InputEvent @event)
    {
        
        // 1. 鼠标点击与拖拽状态转换 [cite: 622]
        if (@event is InputEventMouseButton mb)
        {
            // 【新增】：右键点击检测 (用于下达移动指令)
            if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
            {
                Vector2 screenPos = GetViewport().GetMousePosition();
                Vector2 localMousePos = ToLocal(GetGlobalMousePosition() - (GetViewport().GetMousePosition() - screenPos));
                Vector2I mapCell = _groundLayer.LocalToMap(localMousePos);
                var cubeCoords = MapRenderBridge.OffsetToCube(mapCell.X, mapCell.Y);
                
                OnRightClicked?.Invoke(new HexCoord(cubeCoords.cubeX, cubeCoords.cubeY, cubeCoords.cubeZ));
                GetViewport().SetInputAsHandled();
                return;
            }

            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    _dragStartPos = GetViewport().GetMousePosition(); // [cite: 688]
                    _dragStartGlobalPos = GetGlobalMousePosition();   // 【新增】：记录此时的真实物理坐标
                    _isDragging = true; // [cite: 689]
                }
                else if (_isDragging)
                {
                    _isDragging = false; // [cite: 690]
                    if (_selectionBox != null) _selectionBox.Visible = false; // [cite: 691]

                    Vector2 dragEndPos = GetViewport().GetMousePosition(); // [cite: 692]
                    Vector2 dragEndGlobalPos = GetGlobalMousePosition();   // 【新增】：记录此时真实的物理终点坐标
                    float distance = _dragStartPos.DistanceTo(dragEndPos); // [cite: 693]

                    if (distance < DragThreshold)
                    {
                        HandleSingleClick(dragEndPos); // [cite: 694]
                    }
                    else
                    {
                        // 【修改】：把纯正的物理坐标传进去！
                        HandleBoxSelection(_dragStartGlobalPos, dragEndGlobalPos); 
                    }

                    GetViewport().SetInputAsHandled(); // [cite: 696]
                }
            }
        }

        // 2. 鼠标移动时实时刷新框选框尺寸 [cite: 629]
        if (@event is InputEventMouseMotion mm && _isDragging)
        {
            Vector2 currentPos = GetViewport().GetMousePosition(); // [cite: 629]
            if (_dragStartPos.DistanceTo(currentPos) >= DragThreshold) // [cite: 630]
            {
                UpdateSelectionBox(_dragStartPos, currentPos); // [cite: 630]
            }
        }

        // 3. 按 ESC 取消选中 [cite: 631]
        if (@event is InputEventKey ek && ek.Pressed && ek.Keycode == Key.Escape)
        {
            if (_currentSelectedHex.HasValue)
            {
                ClearSelection(); // [cite: 631]
                OnTileDeselected?.Invoke();  // [cite: 632]
                GetViewport().SetInputAsHandled(); // [cite: 631]
            }
        }
    }

    private void HandleSingleClick(Vector2 screenPos)
    {
        Vector2 localMousePos = ToLocal(GetGlobalMousePosition() - (GetViewport().GetMousePosition() - screenPos)); // [cite: 632]
        Vector2I mapCell = _groundLayer.LocalToMap(localMousePos); // [cite: 633]
        var cubeCoords = MapRenderBridge.OffsetToCube(mapCell.X, mapCell.Y); // [cite: 633]
        var clickedHex = new HexCoord(cubeCoords.cubeX, cubeCoords.cubeY, cubeCoords.cubeZ); // [cite: 633]

        if (CoreHost.MapConfig.Tiles.TryGetValue(clickedHex, out var tileData)) // [cite: 634]
        {
            if (_currentSelectedHex.HasValue && _currentSelectedHex.Value == clickedHex) // [cite: 634]
            {
                ClearSelection(); // [cite: 634]
                OnTileDeselected?.Invoke(); // [cite: 635]
            }
            else
            {
                _currentSelectedHex = clickedHex; // [cite: 635]
                if (_selectMark != null)
                {
                    _selectMark.GlobalPosition = _groundLayer.MapToLocal(mapCell) + _groundLayer.GlobalPosition; // [cite: 636]
                    _selectMark.Visible = true; // [cite: 637]
                }
                OnTileSelected?.Invoke(clickedHex, tileData); // [cite: 637]
            }
        }
    }

    // 【修改】：参数名改为 worldStart 和 worldEnd 避免歧义
    private void HandleBoxSelection(Vector2 worldStart, Vector2 worldEnd)
    {
        // 删掉原本那两行错误的 GetGlobalMousePosition() 减法

        // 直接用纯正的世界坐标计算矩形的上下左右边界
        float minX = Mathf.Min(worldStart.X, worldEnd.X);
        float maxX = Mathf.Max(worldStart.X, worldEnd.X);
        float minY = Mathf.Min(worldStart.Y, worldEnd.Y);
        float maxY = Mathf.Max(worldStart.Y, worldEnd.Y);
        
        Rect2 selectionRect = new Rect2(minX, minY, maxX - minX, maxY - minY);

        // 将这个绝对准确的物理矩形抛给 MainGameView
        OnBoxSelectedRect?.Invoke(selectionRect);
    }

    private void UpdateSelectionBox(Vector2 start, Vector2 end)
    {
        if (_selectionBox == null) return; // [cite: 645]
        float x = Mathf.Min(start.X, end.X); // [cite: 646]
        float y = Mathf.Min(start.Y, end.Y); // [cite: 646]
        float width = Mathf.Abs(start.X - end.X); // [cite: 646]
        float height = Mathf.Abs(start.Y - end.Y); // [cite: 647]

        _selectionBox.Position = new Vector2(x, y); // [cite: 647]
        _selectionBox.Size = new Vector2(width, height); // [cite: 647]
        _selectionBox.Visible = true; // [cite: 648]
    }

    public void ClearSelection()
    {
        _currentSelectedHex = null; // [cite: 649]
        if (_selectMark != null) _selectMark.Visible = false; // [cite: 650]
    }
}