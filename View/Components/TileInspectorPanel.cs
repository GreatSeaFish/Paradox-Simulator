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
    public event Action? OnCloseRequested;

    public override void _Ready()
    {
        _coordLabel = GetNode<Label>("VBoxContainer/CoordLabel");
        _terrainLabel = GetNode<Label>("VBoxContainer/TerrainLabel");
        _actionButton = GetNode<Button>("VBoxContainer/ActionButton");
        _closeButton = GetNode<Button>("VBoxContainer/CloseButton");

        _closeButton.Pressed += () => OnCloseRequested?.Invoke();
        _actionButton.Pressed += OnActionClicked;
        
        Visible = false;
    }

    public void Inspect(HexCoord coord, HexTileData tileData)
    {
        _currentSelectedHex = coord;
        _coordLabel.Text = $"坐标: ({coord.X}, {coord.Y}, {coord.Z})";
        _terrainLabel.Text = $"地形: {tileData.GroundType} (地表: {tileData.SurfaceType})";
        
        // 获取全局逻辑状态
        var state = CoreHost.WorldSimulationState;
        int myId = CoreHost.LocalContext.MyPlayerId;
        
        // 1. 如果地块正在被殖民中
        if (state.ActiveColonizations.TryGetValue(coord, out var task))
        {
            _actionButton.Visible = false; 
            string taskOwner = task.PlayerId == myId ? "我" : $"玩家 {task.PlayerId}";
            _terrainLabel.Text += $"\n\n[进度] {taskOwner} 正在殖民中...\n剩余: {task.RemainingDays} 天";
        }
        // 2. 如果地块已经名花有主
        else if (state.TileOwners.TryGetValue(coord, out int ownerId) && ownerId != -1)
        {
            _actionButton.Visible = false;
            string ownerName = ownerId == myId ? "我" : $"玩家 {ownerId}";
            _terrainLabel.Text += $"\n\n[归属] 已被 {ownerName} 占领";
        }
        // 3. 无主之地，进行相邻判定
        else
        {
            bool isAdjacent = false;
            foreach (var neighbor in HexUtility.GetAllNeighbors(coord))
            {
                if (state.TileOwners.TryGetValue(neighbor, out int nOwnerId) && nOwnerId == myId)
                {
                    isAdjacent = true; 
                    break;
                }
            }
            
            _actionButton.Visible = true;
            if (isAdjacent)
            {
                _actionButton.Text = "建立殖民地 (100天 | -1G/月)";
                _actionButton.Disabled = false;
            }
            else
            {
                _actionButton.Text = "无法殖民 (需与领地相邻)";
                _actionButton.Disabled = true;
            }
        }
        
        Visible = true;
    }

    private void OnActionClicked()
    {
        // 点击后立即向服务器发包
        CoreHost.CommandSender.SendColonizeCommand(_currentSelectedHex);
        
        // 临时禁用按钮，防止帧同步广播回来前玩家疯狂连点
        _actionButton.Disabled = true; 
        _actionButton.Text = "指令已发送...";
    }
}