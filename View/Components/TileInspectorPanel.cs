// res://View/Components/TileInspectorPanel.cs
using Godot;
using System;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;

public partial class TileInspectorPanel : PanelContainer
{
    private Label _coordLabel = null!;
    private Label _terrainLabel = null!;
    private Button _actionButton = null!;
    private Button _closeButton = null!;
    
    private HexCoord _currentSelectedHex;

    // 向外抛出“用户点击了关闭按钮”的动作通知，让顶层控制器去清理地图选择状态
    public event Action? OnCloseRequested;

    public override void _Ready()
    {
        _coordLabel = GetNode<Label>("VBoxContainer/CoordLabel");
        _terrainLabel = GetNode<Label>("VBoxContainer/TerrainLabel");
        _actionButton = GetNode<Button>("VBoxContainer/ActionButton");
        _closeButton = GetNode<Button>("VBoxContainer/CloseButton");

        // 绑定自身按钮事件
        _closeButton.Pressed += () => OnCloseRequested?.Invoke();
        _actionButton.Pressed += OnActionClicked;
        
        // 默认初始化时隐藏自身
        Visible = false;
    }

    /// <summary>
    /// 公开的属性注入接口：传入地块数据，面板自己负责渲染显示
    /// </summary>
    public void Inspect(HexCoord coord, HexTileData tileData)
    {
        _currentSelectedHex = coord;
        _coordLabel.Text = $"坐标: ({coord.X}, {coord.Y}, {coord.Z})";
        _terrainLabel.Text = $"地形: {tileData.GroundType} (地表: {tileData.SurfaceType})";
        _actionButton.Text = "测试地块操作";
        
        Visible = true;
    }

    private void OnActionClicked()
    {
        GD.Print($"[Client UI] 触发了对地块 {_currentSelectedHex.X}, {_currentSelectedHex.Y} 的本地操作按钮！");
        // 未来可以在这里扩展发送特定的业务逻辑指令包
    }
}