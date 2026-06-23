// res://View/MainGameView.cs
using Godot;
using System.Collections.Generic;
using FixedMath.NET;
using ParadoxSimulator.Simulation.State;
using Shared.Math;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;

public partial class MainGameView : Node
{
    // ===== 1. 解耦出来的子系统与交互组件 =====
    private MapInputHandler _inputHandler = null!;
    private TerritoryVisualizer _territoryVisualizer = null!;
    private TileInspectorPanel _inspectorPanel = null!;
// 【新增】用于管理漂浮在地块上的殖民进度 Label 字典
    private readonly Dictionary<HexCoord, Label> _colonizationLabels = new();
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
    // 【新增】预加载军事单位预制体
    private PackedScene _militaryTokenScene = GD.Load<PackedScene>("res://View/MapTokens/military_token.tscn");
    
    // 【新增】用于挂载实体部队的根节点
    private Node2D _unitsRoot = null!;
    
    // 【新增】造兵进度 Label 字典与已部署的部队节点字典
    private readonly Dictionary<HexCoord, Label> _unitBuildLabels = new();
    private readonly Dictionary<HexCoord, Node2D> _spawnedUnitNodes = new();
    

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
        // 【新增】监听地块归属权变更信号，触发领土网格重绘！
        if (CoreHost.WorldSimulationState != null)
        {
            CoreHost.WorldSimulationState.OnTileOwnershipChanged += OnTileOwnershipChangedSync;
        }
        // E. 驱动虚拟时间与日历系统绑定
        _timeFlowRateTab.TabClicked += OnTimeTabClicked;
        if (CoreHost.TimeSystem != null)
        {
            CoreHost.WorldSimulationState.OnSpeedChanged += OnSpeedChangedSync;
            CoreHost.WorldSimulationState.OnDateChanged += OnDateChangedSync;
            UpdateCalendarText(CoreHost.WorldSimulationState.CurrentDate);
        }
        _timeFlowRateTab.CurrentTab = 0; 
// B. 动态挂载/装配我们在第一、二阶段抽离的独立解耦类 (在这段下面新增获取Units节点)
        _unitsRoot = GetNode<Node2D>("Units");

        // D. 桥接横向组件的信号 (在里面补充事件)
        if (CoreHost.WorldSimulationState != null)
        {
            CoreHost.WorldSimulationState.OnTileOwnershipChanged += OnTileOwnershipChangedSync;
            // 【新增】监听逻辑层派发的部队部署事件
            CoreHost.WorldSimulationState.OnUnitSpawned += OnUnitSpawnedSync;
        }
        
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
        // 【新增】每帧实时刷新悬浮文字！
        // 这样即使游戏处于暂停(0速)状态，刚发起的殖民任务文字也能瞬间弹出来
        UpdateColonizationFloatingTexts();
        UpdateUnitBuildFloatingTexts(); // 【新增】实时刷新造兵进度
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

    /// <summary>
    /// 【新增】动态维护地图上的殖民倒计时文字
    /// </summary>
    private void UpdateColonizationFloatingTexts()
    {
        var state = CoreHost.WorldSimulationState;
        
        // 1. 垃圾回收
        var keysToRemove = new List<HexCoord>();
        foreach (var kvp in _colonizationLabels)
        {
            if (!state.ActiveColonizations.ContainsKey(kvp.Key))
            {
                kvp.Value.QueueFree();
                keysToRemove.Add(kvp.Key);
            }
        }
        foreach (var key in keysToRemove) _colonizationLabels.Remove(key);
        
        // 2. 渲染更新
        foreach (var kvp in state.ActiveColonizations)
        {
            HexCoord coord = kvp.Key;
            WorldSimulationState.ColonizationTask task = kvp.Value;
            
            if (!_colonizationLabels.TryGetValue(coord, out Label label))
            {
                label = new Label();
                label.AddThemeFontSizeOverride("font_size", 22);
                label.AddThemeColorOverride("font_color", Colors.White);
                label.AddThemeColorOverride("font_outline_color", Colors.Black);
                label.AddThemeConstantOverride("outline_size", 4);
                
                // 【核心修复】将 ZIndex 设置极高，强行穿透所有地表和领土染色层！
                label.ZIndex = 100; 
                
                var offset = MapRenderBridge.CubeToOffset(coord.X, coord.Y, coord.Z);
                Vector2I cellCoords = new Vector2I(offset.offsetX, offset.offsetY);
                Vector2 localPos = _groundLayer.MapToLocal(cellCoords);
                
                label.Position = localPos + new Vector2(-28, -20);
                _groundLayer.AddChild(label);
                _colonizationLabels[coord] = label;
            }
            
            label.Text = $"{task.RemainingDays}天";
            label.Modulate = task.PlayerId == CoreHost.LocalContext.MyPlayerId ? Colors.LimeGreen : Colors.IndianRed;
        }
        
        if (_inspectorPanel.Visible && state.ActiveColonizations.ContainsKey(_inputHandler.GetCurrentSelectedHex()))
        {
            _inspectorPanel.Inspect(_inputHandler.GetCurrentSelectedHex(), CoreHost.MapConfig.Tiles[_inputHandler.GetCurrentSelectedHex()]);
        }
    }
    
    // 【新增】收到领土变更通知，一键重绘所有人的领土色块
    private void OnTileOwnershipChangedSync(HexCoord coord, int newOwnerId)
    {
        _territoryVisualizer.UpdateOwnershipVisuals();
    }
    
    /// <summary>
    /// 【新增】接收到逻辑层的部署指令后，在场景中真正生成预制体
    /// </summary>
    private void OnUnitSpawnedSync(HexCoord coord, int ownerId, int headcount)
    {
        if (_militaryTokenScene.Instantiate() is Node2D tokenNode)
        {
            // 将六边形坐标转换为屏幕像素坐标
            var offset = MapRenderBridge.CubeToOffset(coord.X, coord.Y, coord.Z);
            Vector2I cellCoords = new Vector2I(offset.offsetX, offset.offsetY);
            Vector2 localPos = _groundLayer.MapToLocal(cellCoords);
            
            // 贴合地块中心
            tokenNode.Position = localPos;
            _unitsRoot.AddChild(tokenNode);
            _spawnedUnitNodes[coord] = tokenNode;

            // 赋值兵力数字
            var headcountLabel = tokenNode.GetNode<Label>("Headcount");
            if (headcountLabel != null) headcountLabel.Text = headcount.ToString();

            // 为底板染色，区分玩家
            var panel = tokenNode.GetNode<Panel>("Panel");
            if (panel != null)
            {
                var playerInfo = CoreHost.LocalContext.LobbyPlayers.Find(p => p.PlayerId == ownerId);
                if (playerInfo != null)
                {
                    // 给 Panel 挂载动态着色方案
                    var styleBox = new StyleBoxFlat { BgColor = _colorValues[playerInfo.ColorId] };
                    panel.AddThemeStyleboxOverride("panel", styleBox);
                }
            }
        }
    }

    /// <summary>
    /// 【新增】动态维护地图上的造兵倒计时文字
    /// </summary>
    private void UpdateUnitBuildFloatingTexts()
    {
        var state = CoreHost.WorldSimulationState;
        
        // 1. 垃圾回收：清理已经完成造兵的废弃 Label
        var keysToRemove = new List<HexCoord>();
        foreach (var kvp in _unitBuildLabels)
        {
            if (!state.ActiveUnitBuilds.ContainsKey(kvp.Key))
            {
                kvp.Value.QueueFree();
                keysToRemove.Add(kvp.Key);
            }
        }
        foreach (var key in keysToRemove) _unitBuildLabels.Remove(key);

        // 2. 渲染更新：创建或刷新正在造兵的 Label
        foreach (var kvp in state.ActiveUnitBuilds)
        {
            HexCoord coord = kvp.Key;
            WorldSimulationState.UnitBuildTask task = kvp.Value;
            
            if (!_unitBuildLabels.TryGetValue(coord, out Label label))
            {
                label = new Label();
                label.AddThemeFontSizeOverride("font_size", 22);
                label.AddThemeColorOverride("font_color", Colors.Yellow); // 使用黄色区别于殖民的白色
                label.AddThemeColorOverride("font_outline_color", Colors.Black);
                label.AddThemeConstantOverride("outline_size", 4);
                
                label.ZIndex = 100;
                var offset = MapRenderBridge.CubeToOffset(coord.X, coord.Y, coord.Z);
                Vector2I cellCoords = new Vector2I(offset.offsetX, offset.offsetY);
                Vector2 localPos = _groundLayer.MapToLocal(cellCoords);
                
                label.Position = localPos + new Vector2(-40, -20);
                _groundLayer.AddChild(label);
                _unitBuildLabels[coord] = label;
            }
            
            label.Text = $"招募:{task.RemainingDays}天";
            label.Modulate = task.PlayerId == CoreHost.LocalContext.MyPlayerId ? Colors.LimeGreen : Colors.IndianRed;
        }
        
        // 如果当前选中面板打开着，且该地块发生进度变化，顺便驱动面板刷新
        if (_inspectorPanel.Visible && state.ActiveUnitBuilds.ContainsKey(_inputHandler.GetCurrentSelectedHex()))
        {
            _inspectorPanel.Inspect(_inputHandler.GetCurrentSelectedHex(), CoreHost.MapConfig.Tiles[_inputHandler.GetCurrentSelectedHex()]);
        }
    }
    public override void _ExitTree()
    {
        // 解绑静态系统事件防泄漏
        if (CoreHost.TimeSystem != null)
        {
            CoreHost.WorldSimulationState.OnSpeedChanged -= OnSpeedChangedSync;
            CoreHost.WorldSimulationState.OnDateChanged -= OnDateChangedSync; 
            // 【新增】注销事件防止内存泄漏
            CoreHost.WorldSimulationState.OnTileOwnershipChanged -= OnTileOwnershipChangedSync;
            // 【新增】注销事件防止内存泄漏
            CoreHost.WorldSimulationState.OnTileOwnershipChanged -= OnTileOwnershipChangedSync;
            CoreHost.WorldSimulationState.OnUnitSpawned -= OnUnitSpawnedSync;
        }
    }
}