// res://View/Components/TileInspectorPanel.cs
using Godot;
using System;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;
using Shared.Protocol;

public partial class TileInspectorPanel : PanelContainer
{
    private Label _coordLabel = null!;
    private Label _terrainLabel = null!;
    private Button _actionButton = null!;
    private Button _closeButton = null!;
    // 【新增】当前按钮的工作模式
    private enum ActionMode { None, Colonize, BuildUnit }
    private ActionMode _currentActionMode = ActionMode.None;
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
            string ownerName = ownerId == myId ? "我" : $"玩家 {ownerId}";
            _terrainLabel.Text += $"\n\n[归属] 已被 {ownerName} 占领";

            if (ownerId == myId)
            {
                _actionButton.Visible = true;
                // 判断当前地块造兵状态
                if (state.ActiveUnitBuilds.TryGetValue(coord, out var buildTask))
                {
                    _actionButton.Text = $"正在招募部队 ({buildTask.RemainingDays}天)";
                    _actionButton.Disabled = true;
                    _currentActionMode = ActionMode.None;
                }
                else if (state.DeployedUnits.ContainsKey(coord))
                {
                    _actionButton.Text = "已有驻军";
                    _actionButton.Disabled = true;
                    _currentActionMode = ActionMode.None;
                }
                else
                {
                    _actionButton.Text = "招募部队 (30天 | -10G)";
                    bool canAfford = state.PlayerFunds.TryGetValue(myId, out int funds) && funds >= 10;
                    _actionButton.Disabled = !canAfford; // 没钱就不让点
                    if (!canAfford) _actionButton.Text += " [资金不足]";
                    _currentActionMode = ActionMode.BuildUnit; // 【标记为造兵模式】
                }
            }
            else 
            {
                // 别人的地块，隐藏操作按钮
                _actionButton.Visible = false;
                _currentActionMode = ActionMode.None;
            }
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
                _currentActionMode = ActionMode.Colonize; // 【标记为殖民模式】
            }
            else
            {
                _actionButton.Text = "无法殖民 (需与领地相邻)";
                _actionButton.Disabled = true;
                _currentActionMode = ActionMode.None;
            }
        }
        
        Visible = true;
    }

    private void OnActionClicked()
    {
        // 根据当前的按钮模式，通过统一的入队接口发送帧同步指令
        if (_currentActionMode == ActionMode.Colonize)
        {
            CoreHost.CommandSender.EnqueueCommand(new PlayerCommand
            {
                InputType = CommandType.Colonize, // 修复点：殖民枚举
                TargetHexX = (short)_currentSelectedHex.X,
                TargetHexY = (short)_currentSelectedHex.Y,
                TargetHexZ = (short)_currentSelectedHex.Z
            });
        }
        else if (_currentActionMode == ActionMode.BuildUnit)
        {
            CoreHost.CommandSender.EnqueueCommand(new PlayerCommand
            {
                InputType = CommandType.BuildUnit, // 修复点：造兵枚举
                TargetHexX = (short)_currentSelectedHex.X,
                TargetHexY = (short)_currentSelectedHex.Y,
                TargetHexZ = (short)_currentSelectedHex.Z
            });
        }
    
        // 临时禁用按钮，防止帧同步广播回来前玩家疯狂连点
        _actionButton.Disabled = true; 
        _actionButton.Text = "指令已发送...";
    }
}