using Godot;
using System;
using System.Linq;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;

public partial class PlayerController : Node
{
    private TabBar _timeFlowRateTab = null!;
    private Label _gameCalendarLabel = null!;
    private Label _fundsAmountLabel = null!;
    private Label _fundsChangeLabel = null!;

    // [新增] 左下角地块属性 UI 节点的引用
    private PanelContainer _tileInfoPanel = null!;
    private Label _coordLabel = null!;
    private Label _terrainLabel = null!;
    private Button _actionButton = null!;
    private Button _closeButton = null!;
    
    private WorldMapRender _mapRender = null!;
    private HexCoord _currentSelectedHex; // 记录当前选中的坐标
    
public override void _Ready()
    {
        _timeFlowRateTab = GetNode<TabBar>("%TimeFlowRate");
        _gameCalendarLabel = GetNode<Label>("%GameCalendar");
        _fundsAmountLabel = GetNode<Label>("Hud/Root/TopBar/FundsBar/Amount");
        _fundsChangeLabel = GetNode<Label>("Hud/Root/TopBar/FundsBar/Change");

        // [新增] 绑定左下角面板的 UI 节点
        _tileInfoPanel = GetNode<PanelContainer>("Hud/Root/TileInfoPanel");
        _coordLabel = _tileInfoPanel.GetNode<Label>("VBoxContainer/CoordLabel");
        _terrainLabel = _tileInfoPanel.GetNode<Label>("VBoxContainer/TerrainLabel");
        _actionButton = _tileInfoPanel.GetNode<Button>("VBoxContainer/ActionButton");
        _closeButton = _tileInfoPanel.GetNode<Button>("VBoxContainer/CloseButton");

        // 默认隐藏面板
        _tileInfoPanel.Visible = false;

        // 按钮事件绑定
        _closeButton.Pressed += OnClosePanelPressed;
        _actionButton.Pressed += OnActionClicked;

        // 监听来自表现层地图的选中事件
        _mapRender = GetParent().GetNode<WorldMapRender>("WorldMapRender");
        if (_mapRender != null)
        {
            _mapRender.OnTileSelected += ShowTileInfoPanel;
            // [新增] 监听地图传递过来的“取消选中”信号，直接关闭 UI 属性面板
            _mapRender.OnTileDeselected += HideTileInfoPanel;
        }
        
        _timeFlowRateTab.TabClicked += OnTimeTabClicked;
        
        // 订阅逻辑层原本的资金、时间事件... (保持原有逻辑不变)
        if (CoreHost.WorldSimulationState != null)
        {
            CoreHost.WorldSimulationState.OnPlayerRealtimeFundsChanged += OnLocalPlayerRealtimeFundsChanged;
            CoreHost.WorldSimulationState.OnPlayerMonthlyExpectedChanged += OnLocalPlayerMonthlyExpectedChanged;
            CoreHost.WorldSimulationState.NotifyFundsChanged(CoreHost.LocalContext.MyPlayerId);
        }
        if (CoreHost.TimeSystem != null)
        {
            CoreHost.TimeSystem.OnSpeedChanged += OnSpeedChangedSync;
            CoreHost.WorldSimulationState.OnDateChanged += OnDateChangedSync;
            UpdateCalendarText(CoreHost.WorldSimulationState.CurrentDate);
        }
        _timeFlowRateTab.CurrentTab = 0;
    }

    // [新增] 弹出属性界面的具体回调
    private void ShowTileInfoPanel(HexCoord coord, HexTileData tileData)
    {
        _currentSelectedHex = coord;

        // 填充地块文本属性
        _coordLabel.Text = $"坐标: ({coord.X}, {coord.Y}, {coord.Z})";
        _terrainLabel.Text = $"地形: {tileData.GroundType} (地表: {tileData.SurfaceType})";
        
        // 设置按钮文本
        _actionButton.Text = "测试地块操作";

        // 显示面板
        _tileInfoPanel.Visible = true;
    }

    // [新增] 纯粹用于隐藏面板的方法（不重复触发地图的 ClearSelection）
    private void HideTileInfoPanel()
    {
        _tileInfoPanel.Visible = false;
    }

    // [更新] 点击 UI 右下角自带的关闭按钮时，依然调用地图的 ClearSelection() 
    private void OnClosePanelPressed()
    {
        _tileInfoPanel.Visible = false;
        _mapRender?.ClearSelection(); // 地图清除后会顺便把 _currentSelectedHex 设为 null
    }
    
    // [新增] 操作按钮点击事件
    private void OnActionClicked()
    {
        GD.Print($"[Client UI] 触发了对地块 {_currentSelectedHex.X}, {_currentSelectedHex.Y} 的本地操作按钮！");
        // 未来若有需要同步给服务器的操作（例如：占领此格子、此处建造建筑等），在此处通过：
        // CoreHost.CommandSender.SendPacket(...) 向服务器传输自定义行为。
    }


    
    
    /// <summary>
    /// 实时变动接收中心：只管更新总额 Amount
    /// </summary>
    private void OnLocalPlayerRealtimeFundsChanged(int playerId, int currentFunds)
    {
        if (playerId != CoreHost.LocalContext.MyPlayerId) return;
        
        if (IsInstanceValid(_fundsAmountLabel))
        {
            _fundsAmountLabel.Text = currentFunds.ToString();
        }
    }

    /// <summary>
    /// 预期/账单变动接收中心：只管更新月度盈亏预测 Change
    /// </summary>
    private void OnLocalPlayerMonthlyExpectedChanged(int playerId, int expectedFundsChange)
    {
        if (playerId != CoreHost.LocalContext.MyPlayerId) return;

        if (IsInstanceValid(_fundsChangeLabel))
        {
            if (expectedFundsChange > 0)
            {
                _fundsChangeLabel.Text = $"+{expectedFundsChange}";
                _fundsChangeLabel.Modulate = Colors.LimeGreen; // 预期盈利显绿色
            }
            else if (expectedFundsChange < 0)
            {
                _fundsChangeLabel.Text = $"{expectedFundsChange}"; // 自带负号
                _fundsChangeLabel.Modulate = Colors.Red;       // 预期赤字显红色
            }
            else
            {
                _fundsChangeLabel.Text = "+0";
                _fundsChangeLabel.Modulate = Colors.White;     // 收支平衡显白色
            }
        }
    }
    
    public override void _ExitTree()
    {
        // 记得注销事件绑定防泄漏
        if (_mapRender != null)
        {
            _mapRender.OnTileSelected -= ShowTileInfoPanel;
            _mapRender.OnTileDeselected -= HideTileInfoPanel; // [新增] 解绑
        }
        _closeButton.Pressed -= OnClosePanelPressed;
        _actionButton.Pressed -= OnActionClicked;

        if (CoreHost.WorldSimulationState != null)
        {
            CoreHost.WorldSimulationState.OnPlayerRealtimeFundsChanged -= OnLocalPlayerRealtimeFundsChanged;
            CoreHost.WorldSimulationState.OnPlayerMonthlyExpectedChanged -= OnLocalPlayerMonthlyExpectedChanged;
        }
        if (CoreHost.TimeSystem != null)
        {
            CoreHost.TimeSystem.OnSpeedChanged -= OnSpeedChangedSync;
            CoreHost.WorldSimulationState.OnDateChanged -= OnDateChangedSync;
        }
    }

    private void OnTimeTabClicked(long tabIndex)
    {
        CoreHost.CommandSender.SendTimeSpeedCommand((int)tabIndex);
    }

    private void OnSpeedChangedSync(int newSpeedLevel)
    {
        if (_timeFlowRateTab.CurrentTab != newSpeedLevel)
        {
            _timeFlowRateTab.CurrentTab = newSpeedLevel;
        }
    }

    private void OnDateChangedSync(DateTime newDate)
    {
        UpdateCalendarText(newDate);
    }

    private void UpdateCalendarText(DateTime date)
    {
        _gameCalendarLabel.Text = $"第{date.Year}年{date.Month}月{date.Day}日";
    }
}