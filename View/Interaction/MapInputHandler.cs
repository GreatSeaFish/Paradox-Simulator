// res://View/Interaction/MapInputHandler.cs
using Godot;
using System;
using System.Collections.Generic;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;

public partial class MapInputHandler : Node2D
{
    private TileMapLayer _groundLayer = null!;
    private Sprite2D _selectMark = null!;
    private ColorRect _selectionBox = null!;

    // 对外暴露的交互事件 [cite: 603, 605]
    public event Action<HexCoord, HexTileData>? OnTileSelected;
    public event Action? OnTileDeselected;

    private bool _isDragging = false;
    private Vector2 _dragStartPos = Vector2.Zero;
    private const float DragThreshold = 5.0f; // [cite: 602]
    private HexCoord? _currentSelectedHex = null; // [cite: 604]

    public void Init(TileMapLayer groundLayer, Sprite2D selectMark, ColorRect selectionBox)
    {
        _groundLayer = groundLayer;
        _selectMark = selectMark;
        _selectionBox = selectionBox;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // 1. 鼠标点击与拖拽状态转换 [cite: 622]
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                _dragStartPos = GetViewport().GetMousePosition(); // [cite: 623]
                _isDragging = true; // [cite: 624]
            }
            else if (_isDragging)
            {
                _isDragging = false; // [cite: 624]
                if (_selectionBox != null) _selectionBox.Visible = false; // [cite: 625]

                Vector2 dragEndPos = GetViewport().GetMousePosition(); // [cite: 625]
                float distance = _dragStartPos.DistanceTo(dragEndPos); // [cite: 625]
                
                if (distance < DragThreshold)
                {
                    HandleSingleClick(dragEndPos); // [cite: 626]
                }
                else
                {
                    HandleBoxSelection(_dragStartPos, dragEndPos); // [cite: 627]
                }
                GetViewport().SetInputAsHandled(); // [cite: 628]
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

    private void HandleBoxSelection(Vector2 start, Vector2 end)
    {
        Vector2 worldStart = GetGlobalMousePosition() - (GetViewport().GetMousePosition() - start); // [cite: 638]
        Vector2 worldEnd = GetGlobalMousePosition() - (GetViewport().GetMousePosition() - end); // [cite: 639]

        float minX = Mathf.Min(worldStart.X, worldEnd.X); // [cite: 639]
        float maxX = Mathf.Max(worldStart.X, worldEnd.X); // [cite: 639]
        float minY = Mathf.Min(worldStart.Y, worldEnd.Y); // [cite: 639]
        float maxY = Mathf.Max(worldStart.Y, worldEnd.Y); // [cite: 640]
        Rect2 selectionRect = new Rect2(minX, minY, maxX - minX, maxY - minY); // [cite: 640]
        List<HexCoord> boxedHexes = new List<HexCoord>(); // [cite: 641]

        foreach (var kvp in CoreHost.MapConfig.Tiles) // [cite: 641]
        {
            var tile = kvp.Value; // [cite: 641]
            var offset = MapRenderBridge.CubeToOffset(tile.X, tile.Y, tile.Z); // [cite: 642]
            Vector2I cellCoords = new Vector2I(offset.offsetX, offset.offsetY); // [cite: 642]
            Vector2 tileWorldPos = _groundLayer.MapToLocal(cellCoords) + _groundLayer.GlobalPosition; // [cite: 643]
            
            if (selectionRect.HasPoint(tileWorldPos)) // [cite: 644]
            {
                boxedHexes.Add(kvp.Key); // [cite: 644]
            }
        }
        // 框选数据可以在这里向外派发事件，未来支持多选
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