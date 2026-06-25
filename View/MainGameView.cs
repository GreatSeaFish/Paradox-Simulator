// res://View/MainGameView.cs
using Godot;
using System.Collections.Generic;
using System.Linq;
using FixedMath.NET;
using ParadoxSimulator.Simulation.State;
using ParadoxSimulator.Simulation.State.WorldModel;
using Shared.Math;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;
using Shared.Protocol;

public partial class MainGameView : Node
{
    // ===== 1. 解耦出来的子系统与交互组件 =====
    private MapInputHandler _inputHandler = null!;
    private TerritoryVisualizer _territoryVisualizer = null!;
    private TileInspectorPanel _inspectorPanel = null!;
    private readonly Dictionary<HexCoord, Label> _colonizationLabels = new();
    private readonly Dictionary<HexCoord, Label> _unitBuildLabels = new();

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

    // ===== 4. 【重构】聚合部队栈渲染系统 =====
    private PackedScene _militaryTokenScene = GD.Load<PackedScene>("res://View/MapTokens/military_token.tscn");
    private Node2D _unitsRoot = null!;
    
    // 【核心改造】：不再 1对1 记录部队节点，而是 1个坐标对应1个栈牌
    private readonly Dictionary<HexCoord, Node2D> _stackNodes = new(); 
    // 快速反查部队所在的坐标，用于移动/移除时的状态更新
    private readonly Dictionary<int, HexCoord> _unitLocationsTracker = new();
    private readonly List<int> _selectedUnitIds = new();

    // ===== 5. 动态生成的左上角部队列表面板 =====
    private PanelContainer _unitStackPanel = null!;
    private VBoxContainer _unitStackList = null!;
    private HexCoord _currentWatchingCoord;

    public override void _Ready()
    {
        _groundLayer = GetNode<TileMapLayer>("WorldMapRender/GroundLayer");
        _timeFlowRateTab = GetNode<TabBar>("Hud/Root/CalendarBar/TimeFlowRate");
        _gameCalendarLabel = GetNode<Label>("Hud/Root/CalendarBar/GameCalendar");

        _inputHandler = GetNode<MapInputHandler>("WorldMapRender/MapInputHandler");
        _territoryVisualizer = GetNode<TerritoryVisualizer>("WorldMapRender/OwnershipFilter");
        _inspectorPanel = GetNode<TileInspectorPanel>("Hud/Root/TileInfoPanel");
        _unitsRoot = GetNode<Node2D>("Units");

        var selectMark = GetNode<Sprite2D>("WorldMapRender/MapInputHandler/SelectMark");
        var selectionBox = GetNode<ColorRect>("Hud/Root/SelectionBox");
        _inputHandler.Init(_groundLayer, selectMark, selectionBox);
        _territoryVisualizer.Init(GetNode<Node2D>("WorldMapRender/OwnershipFilter/TerritorialMarkLayers"));

        _inputHandler.OnTileSelected += (coord, tileData) => _inspectorPanel.Inspect(coord, tileData);
        _inputHandler.OnTileDeselected += () => _inspectorPanel.Visible = false;
        _inspectorPanel.OnCloseRequested += () => {
            _inspectorPanel.Visible = false;
            _inputHandler.ClearSelection();
        };

        _inputHandler.OnRightClicked += OnMapRightClicked;
        _inputHandler.OnBoxSelectedRect += HandleUnitBoxSelection;

        if (CoreHost.WorldSimulationState != null)
        {
            CoreHost.WorldSimulationState.OnTileOwnershipChanged += OnTileOwnershipChangedSync;
            CoreHost.WorldSimulationState.OnUnitSpawned += OnUnitSpawnedSync;
            CoreHost.WorldSimulationState.OnUnitRemoved += OnUnitRemovedSync;
            CoreHost.WorldSimulationState.OnUnitStepped += OnUnitSteppedSync;
            CoreHost.WorldSimulationState.OnSpeedChanged += OnSpeedChangedSync;
            CoreHost.WorldSimulationState.OnDateChanged += OnDateChangedSync;
        }

        _timeFlowRateTab.TabClicked += OnTimeTabClicked;
        if (CoreHost.TimeSystem != null) UpdateCalendarText(CoreHost.WorldSimulationState.CurrentDate);
        _timeFlowRateTab.CurrentTab = 0;

        _territoryVisualizer.UpdateOwnershipVisuals();
        CreatePlayerNodes();
        
        // 【新增】：纯代码动态创建左上角的堆叠部队列表面板
        BuildUnitStackPanel();
    }
    
    // ==========================================
    // =====    动态 UI：部队列表弹窗构建    =====
    // ==========================================
    private void BuildUnitStackPanel()
    {
        _unitStackPanel = new PanelContainer {
            Visible = false,
            Position = new Vector2(20, 80), // 放在左上角，避开资金栏
            CustomMinimumSize = new Vector2(260, 300)
        };
        
        // 挂个深色半透明背景
        _unitStackPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.1f, 0.1f, 0.1f, 0.8f) });

        var mainVBox = new VBoxContainer();
        
        var titleLabel = new Label { Text = "驻军列表", HorizontalAlignment = HorizontalAlignment.Center };
        mainVBox.AddChild(titleLabel);
        
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(250, 300) };
        _unitStackList = new VBoxContainer();
        scroll.AddChild(_unitStackList);
        mainVBox.AddChild(scroll);
        
        var closeBtn = new Button { Text = "关闭" };
        closeBtn.Pressed += () => _unitStackPanel.Visible = false;
        mainVBox.AddChild(closeBtn);

        _unitStackPanel.AddChild(mainVBox);
        GetNode("Hud/Root").AddChild(_unitStackPanel);
    }

    private void ShowUnitStackPanel(HexCoord coord)
    {
        _currentWatchingCoord = coord;
        _unitStackPanel.Visible = true;
        
        // 清理旧列表
        foreach (Node child in _unitStackList.GetChildren()) child.QueueFree();

        var state = CoreHost.WorldSimulationState;
        int myId = CoreHost.LocalContext.MyPlayerId;
        var unitsHere = state.DeployedUnits.Values.Where(u => u.CurrentLocation == coord).ToList();

        if (unitsHere.Count == 0)
        {
            _unitStackPanel.Visible = false;
            return;
        }

        foreach (var unit in unitsHere)
        {
            var hbox = new HBoxContainer();
            hbox.AddChild(new Label { Text = $"部队 {unit.UnitId} | {unit.Headcount}人 " });

            if (unit.OwnerId == myId)
            {
                bool isSelected = _selectedUnitIds.Contains(unit.UnitId);
                var selectBtn = new Button { Text = isSelected ? "取消" : "选中" };
                
                selectBtn.Pressed += () => {
                    if (_selectedUnitIds.Contains(unit.UnitId)) _selectedUnitIds.Remove(unit.UnitId);
                    else _selectedUnitIds.Add(unit.UnitId);
                    
                    UpdateStackVisual(coord); // 刷新外观高亮
                    ShowUnitStackPanel(coord); // 递归刷新面板本身
                };
                hbox.AddChild(selectBtn);
            }
            else
            {
                var enemyLabel = new Label { Text = "[敌军]", Modulate = Colors.Red };
                hbox.AddChild(enemyLabel);
            }
            _unitStackList.AddChild(hbox);
        }
    }

// ==========================================
    // ===== 核心渲染：统一刷新某一地块的栈牌 =====
    // ==========================================
    private void UpdateStackVisual(HexCoord coord)
    {
        var state = CoreHost.WorldSimulationState;
        var unitsHere = state.DeployedUnits.Values.Where(u => u.CurrentLocation == coord).ToList();

        // 1. 如果该地块已经没有部队了，销毁栈牌
        if (unitsHere.Count == 0)
        {
            if (_stackNodes.TryGetValue(coord, out var node))
            {
                if (IsInstanceValid(node)) node.QueueFree();
                _stackNodes.Remove(coord);
            }
            return;
        }

        // 2. 如果还没有该地块的栈牌，实例化一个
        if (!_stackNodes.TryGetValue(coord, out var tokenNode) || !IsInstanceValid(tokenNode))
        {
            tokenNode = _militaryTokenScene.Instantiate<Node2D>();
            _unitsRoot.AddChild(tokenNode);
            _stackNodes[coord] = tokenNode;

            var panel = tokenNode.GetNode<Panel>("Panel");
            if (panel != null)
            {
                panel.GuiInput += (@event) => 
                {
                    if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
                    {
                        // 【核心修改】：点击时默认先清空其他选择，并将该地块上所有属于我的部队加入选中列表
                        ClearUnitSelection();
                        
                        int myId = CoreHost.LocalContext.MyPlayerId;
                        var myUnits = CoreHost.WorldSimulationState.DeployedUnits.Values
                            .Where(u => u.CurrentLocation == coord && u.OwnerId == myId);
                        
                        foreach (var u in myUnits)
                        {
                            _selectedUnitIds.Add(u.UnitId);
                        }

                        UpdateStackVisual(coord); // 立刻刷新当前栈牌的高亮状态
                        ShowUnitStackPanel(coord); // 弹出列表（列表里的按钮会自动显示为“取消”）
                        panel.AcceptEvent(); // 吞噬事件，防止点到后面的地块
                    }
                };
            }
        }

        // 3. 刷新位置（不再需要堆叠偏移量）
        var offset = MapRenderBridge.CubeToOffset(coord.X, coord.Y, coord.Z);
        Vector2I cellCoords = new Vector2I(offset.offsetX, offset.offsetY);
        tokenNode.Position = _groundLayer.MapToLocal(cellCoords);

        // 4. 刷新总人数
        int totalHeadcount = unitsHere.Sum(u => u.Headcount);
        var headcountLabel = tokenNode.GetNode<Label>("Headcount");
        if (headcountLabel != null) headcountLabel.Text = totalHeadcount.ToString();

        // 5. 刷新颜色（提取这个格子上第一支部队的颜色作为代表色）
        var firstUnit = unitsHere.First();
        var playerInfo = CoreHost.LocalContext.LobbyPlayers.Find(p => p.PlayerId == firstUnit.OwnerId);
        if (playerInfo != null)
        {
            var styleBox = new StyleBoxFlat { BgColor = _colorValues[playerInfo.ColorId] };
            tokenNode.GetNode<Panel>("Panel").AddThemeStyleboxOverride("panel", styleBox);
        }

        // 6. 刷新高亮状态（只要这个格子上【有任何一支部队被选中了】，栈牌整体就发光）
        bool hasSelected = unitsHere.Any(u => _selectedUnitIds.Contains(u.UnitId));
        tokenNode.Modulate = hasSelected ? new Color(1.5f, 1.5f, 1.5f, 1.0f) : Colors.White;
    }
    
    
    // ==========================================
    // =====          逻辑事件同步           =====
    // ==========================================
    private void OnUnitSpawnedSync(WorldSimulationState.MilitaryUnit unit)
    {
        _unitLocationsTracker[unit.UnitId] = unit.CurrentLocation;
        UpdateStackVisual(unit.CurrentLocation);
    }

    private void OnUnitSteppedSync(int unitId, HexCoord from, HexCoord to)
    {
        _unitLocationsTracker[unitId] = to;
        UpdateStackVisual(from); // 刷新起点的剩余总人数
        UpdateStackVisual(to);   // 刷新终点的新增总人数
        
        // 如果玩家刚好打开着起点或终点的面板，动态刷新它
        if (_unitStackPanel.Visible && (_currentWatchingCoord == from || _currentWatchingCoord == to))
        {
            ShowUnitStackPanel(_currentWatchingCoord);
        }
    }

    private void OnUnitRemovedSync(int unitId)
    {
        _selectedUnitIds.Remove(unitId);
        if (_unitLocationsTracker.TryGetValue(unitId, out var coord))
        {
            _unitLocationsTracker.Remove(unitId);
            UpdateStackVisual(coord); // 刷新该地块
            if (_unitStackPanel.Visible && _currentWatchingCoord == coord) ShowUnitStackPanel(coord);
        }
    }

    private void HandleUnitBoxSelection(Rect2 selectionRect)
    {
        ClearUnitSelection();
        var state = CoreHost.WorldSimulationState;
        int myId = CoreHost.LocalContext.MyPlayerId;

        // 遍历所有栈牌
        foreach (var kvp in _stackNodes)
        {
            if (IsInstanceValid(kvp.Value) && selectionRect.HasPoint(kvp.Value.GlobalPosition))
            {
                // 把这个格子上所有属于我的部队 ID 塞进选中列表
                var unitsHere = state.DeployedUnits.Values.Where(u => u.CurrentLocation == kvp.Key && u.OwnerId == myId);
                foreach (var unit in unitsHere)
                {
                    _selectedUnitIds.Add(unit.UnitId);
                }
                UpdateStackVisual(kvp.Key); // 更新高亮
            }
        }
    }

    private void OnMapRightClicked(HexCoord targetHex)
    {
        if (_selectedUnitIds.Count == 0) return;
        var state = CoreHost.WorldSimulationState;

        foreach (var unitId in _selectedUnitIds)
        {
            if (!state.DeployedUnits.TryGetValue(unitId, out var unit)) continue;
            if (unit.CurrentLocation == targetHex) continue;

            CoreHost.CommandSender.EnqueueCommand(new PlayerCommand
            {
                InputType = CommandType.UnitMove,
                ActionValue = unitId, 
                TargetHexX = (short)targetHex.X,
                TargetHexY = (short)targetHex.Y,
                TargetHexZ = (short)targetHex.Z
            });
        }
    }

    private void ClearUnitSelection()
    {
        _selectedUnitIds.Clear();
        foreach (var node in _stackNodes.Values)
        {
            if (IsInstanceValid(node)) node.Modulate = Colors.White;
        }
        if (_unitStackPanel != null) _unitStackPanel.Visible = false; // 取消选择时顺手关掉面板
    }

    private void UpdateUnitMoveProgressBars()
    {
        var state = CoreHost.WorldSimulationState;
        if (state == null) return;

        // 遍历所有地块的栈牌
        foreach (var kvp in _stackNodes)
        {
            if (!IsInstanceValid(kvp.Value)) continue;
            var pBar = kvp.Value.GetNode<ProgressBar>("MoveProgressBar");
            
            if (pBar != null)
            {
                // 【核心修改】：默认先将进度条归零并隐藏
                pBar.Value = 0;
                pBar.Visible = false;

                // 找出这个地块上正在移动的部队（选进度跑得最快的那支来作为代表展示）
                var unitsHere = state.DeployedUnits.Values.Where(u => u.CurrentLocation == kvp.Key);
                foreach (var unit in unitsHere)
                {
                    var task = state.ActiveUnitMoves.Values.FirstOrDefault(m => m.UnitId == unit.UnitId);
                    if (task != null && task.TotalDaysForNextTile > 0)
                    {
                        float progress = (1.0f - (float)task.RemainingDaysForNextTile / task.TotalDaysForNextTile) * 100f;
                        pBar.Value = progress;
                        
                        // 【核心修改】：只要找到该地块上有任意一支部队在移动，就把进度条显示出来
                        pBar.Visible = true; 
                        break; // 找到一个就足够显示了，直接 break
                    }
                }
            }
        }
    }
    
    
    // ==========================================
    // =====    原有的 _Process 和辅助逻辑    =====
    // ==========================================
    public override void _Process(double delta)
    {
        UpdateColonizationFloatingTexts();
        UpdateUnitBuildFloatingTexts(); 

        Vector2 godotDir = Input.GetVector("a", "d", "w", "s");
        godotDir.Y = -godotDir.Y;
        CoreHost.LocalContext.SetLocalInput(godotDir != Vector2.Zero ? new FixVector2((Fix64)godotDir.X, (Fix64)godotDir.Y) : FixVector2.Zero);

        CoreHost.WorldSimulationState.GetLogicalPositions(_currentPositions);
        float worldScale = 50f;
        Vector2 screenCenter = new Vector2(500f, 400f);
        foreach (var pair in _playerNodes) 
        {
            if (IsInstanceValid(pair.Value) && _currentPositions.ContainsKey(pair.Key)) 
            {
                FixVector2 currentFix = _currentPositions[pair.Key];
                pair.Value.Position = new Vector2(screenCenter.X + ((float)currentFix.X * worldScale) - 15f, screenCenter.Y - ((float)currentFix.Y * worldScale) - 15f); 
            }
        }
        UpdateUnitMoveProgressBars();
    }

    private void CreatePlayerNodes()
    {
        foreach (var player in CoreHost.LocalContext.LobbyPlayers) 
        {
            var rect = new ColorRect { Size = new Vector2(30f, 30f), Color = _colorValues[player.ColorId] };
            AddChild(rect); 
            _playerNodes[player.PlayerId] = rect; 
            _currentPositions[player.PlayerId] = FixVector2.Zero; 
        }
    }

    private void OnTimeTabClicked(long tabIndex)
    {
        CoreHost.CommandSender.EnqueueCommand(new PlayerCommand { InputType = CommandType.TimeSpeedControl, ActionValue = (int)tabIndex });
    }

    private void OnSpeedChangedSync(int newSpeedLevel)
    {
        if (_timeFlowRateTab.CurrentTab != newSpeedLevel) _timeFlowRateTab.CurrentTab = newSpeedLevel;
    }

    private void OnDateChangedSync(GameDateTime newDate) => UpdateCalendarText(newDate);

    private void UpdateCalendarText(GameDateTime date) => _gameCalendarLabel.Text = $"第{date.Year}年{date.Month}月{date.Day}日";

    private void OnTileOwnershipChangedSync(HexCoord coord, int newOwnerId) => _territoryVisualizer.UpdateOwnershipVisuals();

    private void UpdateColonizationFloatingTexts()
    {
        var state = CoreHost.WorldSimulationState;
        var keysToRemove = _colonizationLabels.Keys.Where(k => !state.ActiveColonizations.ContainsKey(k)).ToList();
        foreach (var key in keysToRemove) { _colonizationLabels[key].QueueFree(); _colonizationLabels.Remove(key); }
        
        foreach (var kvp in state.ActiveColonizations)
        {
            if (!_colonizationLabels.TryGetValue(kvp.Key, out Label label))
            {
                label = new Label();
                label.AddThemeFontSizeOverride("font_size", 22);
                label.AddThemeColorOverride("font_color", Colors.White);
                label.AddThemeColorOverride("font_outline_color", Colors.Black);
                label.AddThemeConstantOverride("outline_size", 4);
                label.ZIndex = 100;
                var offset = MapRenderBridge.CubeToOffset(kvp.Key.X, kvp.Key.Y, kvp.Key.Z);
                label.Position = _groundLayer.MapToLocal(new Vector2I(offset.offsetX, offset.offsetY)) + new Vector2(-28, -20);
                _groundLayer.AddChild(label);
                _colonizationLabels[kvp.Key] = label;
            }
            label.Text = $"{kvp.Value.RemainingDays}天";
            label.Modulate = kvp.Value.PlayerId == CoreHost.LocalContext.MyPlayerId ? Colors.LimeGreen : Colors.IndianRed;
        }
    }

    private void UpdateUnitBuildFloatingTexts()
    {
        var state = CoreHost.WorldSimulationState;
        var keysToRemove = _unitBuildLabels.Keys.Where(k => !state.ActiveUnitBuilds.ContainsKey(k)).ToList();
        foreach (var key in keysToRemove) { _unitBuildLabels[key].QueueFree(); _unitBuildLabels.Remove(key); }
        
        foreach (var kvp in state.ActiveUnitBuilds)
        {
            if (!_unitBuildLabels.TryGetValue(kvp.Key, out Label label))
            {
                label = new Label();
                label.AddThemeFontSizeOverride("font_size", 22);
                label.AddThemeColorOverride("font_color", Colors.Yellow);
                label.AddThemeColorOverride("font_outline_color", Colors.Black);
                label.AddThemeConstantOverride("outline_size", 4);
                label.ZIndex = 100;
                var offset = MapRenderBridge.CubeToOffset(kvp.Key.X, kvp.Key.Y, kvp.Key.Z);
                label.Position = _groundLayer.MapToLocal(new Vector2I(offset.offsetX, offset.offsetY)) + new Vector2(-40, -20);
                _groundLayer.AddChild(label);
                _unitBuildLabels[kvp.Key] = label;
            }
            label.Text = $"招募:{kvp.Value.RemainingDays}天";
            label.Modulate = kvp.Value.PlayerId == CoreHost.LocalContext.MyPlayerId ? Colors.LimeGreen : Colors.IndianRed;
        }
    }

    public override void _ExitTree()
    {
        if (CoreHost.TimeSystem != null)
        {
            CoreHost.WorldSimulationState.OnUnitStepped -= OnUnitSteppedSync;
            CoreHost.WorldSimulationState.OnSpeedChanged -= OnSpeedChangedSync;
            CoreHost.WorldSimulationState.OnDateChanged -= OnDateChangedSync; 
            CoreHost.WorldSimulationState.OnTileOwnershipChanged -= OnTileOwnershipChangedSync;
            CoreHost.WorldSimulationState.OnUnitSpawned -= OnUnitSpawnedSync;
            CoreHost.WorldSimulationState.OnUnitRemoved -= OnUnitRemovedSync;
            if (_inputHandler != null)
            {
                _inputHandler.OnRightClicked -= OnMapRightClicked;
                _inputHandler.OnBoxSelectedRect -= HandleUnitBoxSelection;
            }
        }
    }
}