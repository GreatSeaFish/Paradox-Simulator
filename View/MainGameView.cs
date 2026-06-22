// res://View/MainGameView.cs
using Godot;
using System.Collections.Generic;
using FixedMath.NET;
using Shared.Math;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;

public partial class MainGameView : Node
{
    // ===== 1. 解耦出来的子系统与交互组件 =====
    private MapInputHandler _inputHandler = null!;
    private TerritoryVisualizer _territoryVisualizer = null!;
    private TileInspectorPanel _inspectorPanel = null!;

    // ===== 2. 基础渲染节点引用 =====
    private TileMapLayer _groundLayer = null!;
    private TabBar _timeFlowRateTab = null!;
    private Label _gameCalendarLabel = null!;

    // ===== 3. 局内逻辑方块同步核心数据 =====
    private readonly Dictionary<int, ColorRect> _playerNodes = new();
    private readonly Dictionary<int, FixVector2> _currentPositions = new();
    
    private readonly Color[] _colorValues = { 
        Colors.Red, Colors.Blue, Colors.Orange, Colors.Green, 
        Colors.Yellow, Colors.Purple, Colors.Pink, Colors.LightBlue 
    }; 

    public override void _Ready()
    {
        // A. 初始化基础图层与 UI 节点引用
        _groundLayer = GetNode<TileMapLayer>("WorldMapRender/GroundLayer");
        _timeFlowRateTab = GetNode<TabBar>("Hud/Root/CalendarBar/TimeFlowRate");
        _gameCalendarLabel = GetNode<Label>("Hud/Root/CalendarBar/GameCalendar");

        // B. 动态挂载/装配我们在第一、二阶段抽离的独立解耦类
        _inputHandler = GetNode<MapInputHandler>("WorldMapRender/MapInputHandler");
        _territoryVisualizer = GetNode<TerritoryVisualizer>("WorldMapRender/OwnershipFilter");
        _inspectorPanel = GetNode<TileInspectorPanel>("Hud/Root/TileInfoPanel");

        // C. 通过注入必要的依赖完成子组件初始化
        var selectMark = GetNode<Sprite2D>("WorldMapRender/MapInputHandler/SelectMark");
        var selectionBox = GetNode<ColorRect>("Hud/Root/SelectionBox");
        _inputHandler.Init(_groundLayer, selectMark, selectionBox);
        _territoryVisualizer.Init(GetNode<Node2D>("WorldMapRender/OwnershipFilter/TerritorialMarkLayers"));

        // D. 桥接横向组件的信号（让输入处理器直接单向驱动属性面板）
        _inputHandler.OnTileSelected += (coord, tileData) => _inspectorPanel.Inspect(coord, tileData);
        _inputHandler.OnTileDeselected += () => _inspectorPanel.Visible = false;
        
        // 面板点击关闭按钮时，反向通知输入处理器清理地图高亮标记
        _inspectorPanel.OnCloseRequested += () => {
            _inspectorPanel.Visible = false;
            _inputHandler.ClearSelection();
        };

        // E. 驱动虚拟时间与日历系统绑定
        _timeFlowRateTab.TabClicked += OnTimeTabClicked;
        if (CoreHost.TimeSystem != null)
        {
            CoreHost.TimeSystem.OnSpeedChanged += OnSpeedChangedSync;
            CoreHost.WorldSimulationState.OnDateChanged += OnDateChangedSync;
            UpdateCalendarText(CoreHost.WorldSimulationState.CurrentDate);
        }
        _timeFlowRateTab.CurrentTab = 0; 

        // F. 局内首帧画面渲染对齐
        _territoryVisualizer.UpdateOwnershipVisuals();
        CreatePlayerNodes();
    }

    private void CreatePlayerNodes()
    {
        // 根据大厅名单动态生成 Godot 玩家方块 
        foreach (var player in CoreHost.LocalContext.LobbyPlayers) 
        {
            var rect = new Godot.ColorRect
            {
                Size = new Vector2(30f, 30f), 
                Color = _colorValues[player.ColorId] 
            };
            AddChild(rect); 
            _playerNodes[player.PlayerId] = rect; 
            _currentPositions[player.PlayerId] = FixVector2.Zero; 
        }
    }

    public override void _Process(double delta)
    {
        // ==================== 1. 采集输入与发包（保持原帧同步真理） ====================
        Vector2 godotDir = Input.GetVector("a", "d", "w", "s"); 
        godotDir.Y = -godotDir.Y;  // 保持物理世界：上为正，下为负
    
        if (godotDir != Vector2.Zero)
        {
            godotDir = godotDir.Normalized(); 
            CoreHost.LocalContext.SetLocalInput(new FixVector2((Fix64)godotDir.X, (Fix64)godotDir.Y)); 
        }
        else
        {
            CoreHost.LocalContext.SetLocalInput(FixVector2.Zero); 
        }

        // ==================== 2. 映射确定性绝对逻辑位置到屏幕坐标 ====================
        CoreHost.WorldSimulationState.GetLogicalPositions(_currentPositions); 
        float worldScale = 50f;  // 缩放因子：1个逻辑单位 = 50个屏幕像素
        Vector2 screenCenter = new Vector2(500f, 400f); 

        foreach (var pair in _playerNodes) 
        {
            int playerId = pair.Key; 
            ColorRect rectNode = pair.Value; 

            if (IsInstanceValid(rectNode) && _currentPositions.ContainsKey(playerId)) 
            {
                FixVector2 currentFix = _currentPositions[playerId]; 
                float screenX = screenCenter.X + ((float)currentFix.X * worldScale); 
                float screenY = screenCenter.Y - ((float)currentFix.Y * worldScale); 

                rectNode.Position = new Vector2(screenX - 15f, screenY - 15f); 
            }
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

    private void OnDateChangedSync(System.DateTime newDate)
    {
        UpdateCalendarText(newDate); 
    }

    private void UpdateCalendarText(System.DateTime date)
    {
        _gameCalendarLabel.Text = $"第{date.Year}年{date.Month}月{date.Day}日"; 
    }

    public override void _ExitTree()
    {
        // 解绑静态系统事件防泄漏
        if (CoreHost.TimeSystem != null)
        {
            CoreHost.TimeSystem.OnSpeedChanged -= OnSpeedChangedSync; 
            CoreHost.WorldSimulationState.OnDateChanged -= OnDateChangedSync; 
        }
    }
}