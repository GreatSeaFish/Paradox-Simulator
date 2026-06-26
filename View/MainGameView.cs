// res://View/MainGameView.cs
using Godot;
using System.Collections.Generic;
using System.Linq;
using FixedMath.NET;
using ParadoxSimulator.Simulation;
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
    private readonly Dictionary<HexCoord, ProgressBar> _occupationProgressBars = new(); // 【新增】占领进度条缓存

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
    private PackedScene _battleTokenScene = GD.Load<PackedScene>("res://View/MapTokens/battle_token.tscn"); // 【新增】
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
            // 在 _Ready() 的 if (CoreHost.WorldSimulationState != null) 块中添加：
            CoreHost.WorldSimulationState.OnMonthlySettlementRequired += OnMonthlySettlementRequiredSync;
            CoreHost.WorldSimulationState.OnTileOwnershipChanged += OnTileOwnershipChangedSync;
            CoreHost.WorldSimulationState.OnUnitSpawned += OnUnitSpawnedSync;
            CoreHost.WorldSimulationState.OnUnitRemoved += OnUnitRemovedSync;
            CoreHost.WorldSimulationState.OnUnitStepped += OnUnitSteppedSync;
            CoreHost.WorldSimulationState.OnSpeedChanged += OnSpeedChangedSync;
            CoreHost.WorldSimulationState.OnDateChanged += OnDateChangedSync;
            
            CoreHost.WorldSimulationState.OnCombatStarted += OnCombatStartedSync;
            CoreHost.WorldSimulationState.OnCombatUpdated += OnCombatUpdatedSync;
            CoreHost.WorldSimulationState.OnCombatEnded += OnCombatEndedSync;
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
    // 声明一个类成员变量来持有这个合并按钮的引用，方便后续动态控制它
    private Button _stackMergeBtn = null!; // 【新增类成员变量，请写在类顶部】

    private void BuildUnitStackPanel()
    {
        _unitStackPanel = new PanelContainer {
            Visible = false,
            Position = new Vector2(20, 80),
            CustomMinimumSize = new Vector2(260, 300)
        };
    
        _unitStackPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.1f, 0.1f, 0.1f, 0.8f) });
        var mainVBox = new VBoxContainer();
    
        // --- 【修改部分】将原本顶部的单行 Label 改为水平容器，把“合并”按钮并排塞进去 ---
        var topHBox = new HBoxContainer();
        var titleLabel = new Label { Text = "驻军列表 ", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    
        _stackMergeBtn = new Button { Text = "合并", TooltipText = "将当前地块的所有我方残兵整合成满编" };
        // 绑定点击事件：直接对当前正在查看的六边形坐标发送合兵网络指令
        _stackMergeBtn.Pressed += () => {
            CoreHost.CommandSender.EnqueueCommand(new PlayerCommand
            {
                InputType = CommandType.MergeUnits,
                TargetHexX = (short)_currentWatchingCoord.X,
                TargetHexY = (short)_currentWatchingCoord.Y,
                TargetHexZ = (short)_currentWatchingCoord.Z
            });
            _unitStackPanel.Visible = false; // 发送后先关闭面板，静待网络逻辑帧广播回来自动刷新
        };
    
        topHBox.AddChild(titleLabel);
        topHBox.AddChild(_stackMergeBtn);
        mainVBox.AddChild(topHBox);
        // -----------------------------------------------------------------

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

        // --- 【新增控制逻辑】根据当前地块我方可合并的单位数量，动态亮起/灰化合并按钮 ---
        var myUnitsHere = unitsHere.Where(u => u.OwnerId == myId && u.Headcount > 0).ToList();
    
        // 只有我方单位大于等于2个，且当前没有发生战斗时，才允许在列表中点击合并
        bool isCombatActive = state.ActiveCombats.Values.Any(c => c.Location == coord);
        if (myUnitsHere.Count >= 2 && !isCombatActive)
        {
            _stackMergeBtn.Disabled = false;
            _stackMergeBtn.Modulate = Colors.LimeGreen; // 亮绿色提示可用
        }
        else
        {
            _stackMergeBtn.Disabled = true;
            _stackMergeBtn.Modulate = Colors.Gray;      // 灰化不可用
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
/// <summary>
/// 核心渲染：统一刷新某一地块的栈牌（支持常规集团军展示与聚合战场 BattleToken 动态切换）
/// </summary>
private void UpdateStackVisual(HexCoord coord)
{
    var state = CoreHost.WorldSimulationState;
    var unitsHere = state.DeployedUnits.Values.Where(u => u.CurrentLocation == coord).ToList();

    // 1. 如果该地块已经没有部队了，销毁栈牌并移出追踪字典
    if (unitsHere.Count == 0)
    {
        if (_stackNodes.TryGetValue(coord, out var node))
        {
            if (IsInstanceValid(node)) node.QueueFree();
            _stackNodes.Remove(coord);
        }
        return;
    }

    // 检查当前地块是否存在正在进行的聚合战场
    bool isCombatActive = state.ActiveCombats.Values.Any(c => c.Location == coord);

    // 2. 检查是否需要替换 Token 类型 (和平 -> 战斗，或 战斗 -> 和平)
    bool needsNewToken = true;
    if (_stackNodes.TryGetValue(coord, out var existingToken) && IsInstanceValid(existingToken))
    {
        // 通过节点名称或挂载节点特征判断当前实例的类型是否与战场状态匹配
        bool isExistingTokenBattle = existingToken.Name.ToString().Contains("BattleToken");
        if (isExistingTokenBattle == isCombatActive)
        {
            needsNewToken = false; // 类型完全匹配，不需要重新实例化，只需更新数据
        }
        else
        {
            // 类型不匹配（例如战斗刚刚结束或刚刚爆发），直接销毁旧的，准备下方重建
            existingToken.QueueFree();
            _stackNodes.Remove(coord);
        }
    }

    // 3. 实例化对应的 Token 预制体
    if (needsNewToken)
    {
        Node2D tokenNode = isCombatActive ? _battleTokenScene.Instantiate<Node2D>() : _militaryTokenScene.Instantiate<Node2D>();
        _unitsRoot.AddChild(tokenNode);
        _stackNodes[coord] = tokenNode;

        // 动态绑定点击事件 (兼容两种 Token 不同的 UI 按钮面板结构)
        var panels = isCombatActive ? 
            new[] { tokenNode.GetNode<Panel>("UnitLift/Panel"), tokenNode.GetNode<Panel>("UnitRight/Panel") } : 
            new[] { tokenNode.GetNode<Panel>("Panel") };

        foreach (var panel in panels)
        {
            if (panel != null)
            {
                // 【新增】：强制将面板的鼠标过滤模式设为 Pass 
                // 这样左键依然能响应 GuiInput，但右键会安全地漏给下层的地图和 MapInputHandler
                panel.MouseFilter = Control.MouseFilterEnum.Pass;
                
                panel.GuiInput += (@event) => 
                {
                    if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
                    {
                        // 点击时默认先清空其他选择，并将该地块上所有属于我的部队加入选中列表
                        ClearUnitSelection();
                        int myId = CoreHost.LocalContext.MyPlayerId;
                        var myUnits = CoreHost.WorldSimulationState.DeployedUnits.Values
                            .Where(u => u.CurrentLocation == coord && u.OwnerId == myId);
                        foreach (var u in myUnits) _selectedUnitIds.Add(u.UnitId);

                        UpdateStackVisual(coord); // 刷新高亮外观
                        ShowUnitStackPanel(coord); // 打开左上角详细驻军列表面板
                        panel.AcceptEvent();       // 吞噬点击事件，防止点穿到下层地块
                    }
                };
            }
        }
    }

    // 4. 刷新战场逻辑坐标到游戏世界物理坐标的转换
    var currentToken = _stackNodes[coord];
    var offset = MapRenderBridge.CubeToOffset(coord.X, coord.Y, coord.Z);
    currentToken.Position = _groundLayer.MapToLocal(new Vector2I(offset.offsetX, offset.offsetY));

    // 5. 根据 Token 的具体形态，渲染对应的聚合数据
    if (isCombatActive)
    {
        // ==========================================
        // 【聚合战场表现】：解算并渲染多对多军团对抗属性
        // ==========================================
        if (unitsHere.Count >= 1)
        {
            // 划分战场对抗双方阵营（以格子里的第一支部队为A军团基准，其余敌对势力为B军团）
            int factionA_Id = unitsHere[0].OwnerId;
            var sideA = unitsHere.Where(u => u.OwnerId == factionA_Id).ToList();
            var sideB = unitsHere.Where(u => u.OwnerId != factionA_Id).ToList();

            // 渲染左侧进攻翼（A军团总兵力与平均士气）
            if (sideA.Count > 0)
            {
                int totalHeadA = sideA.Sum(u => u.Headcount);
                float avgMoraleA = (float)sideA.Average(u => u.Morale);
                
                currentToken.GetNode<Label>("UnitLift/Headcount").Text = totalHeadA.ToString();
                currentToken.GetNode<ProgressBar>("UnitLift/MoraleProgressBar").Value = avgMoraleA / 1000 * 100;
                
                var aInfo = CoreHost.LocalContext.LobbyPlayers.Find(p => p.PlayerId == factionA_Id);
                if (aInfo != null) currentToken.GetNode<Panel>("UnitLift/Panel").AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = _colorValues[aInfo.ColorId] });
            }

            // 渲染右侧防守翼（B军团总兵力与平均士气）
            if (sideB.Count > 0)
            {
                int totalHeadB = sideB.Sum(u => u.Headcount);
                float avgMoraleB = (float)sideB.Average(u => u.Morale);
                
                currentToken.GetNode<Label>("UnitRight/Headcount").Text = totalHeadB.ToString();
                currentToken.GetNode<ProgressBar>("UnitRight/MoraleProgressBar").Value = avgMoraleB / 1000 * 100;
                
                var bInfo = CoreHost.LocalContext.LobbyPlayers.Find(p => p.PlayerId == sideB[0].OwnerId);
                if (bInfo != null) currentToken.GetNode<Panel>("UnitRight/Panel").AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = _colorValues[bInfo.ColorId] });
            }
        }
    }
    else
    {
        // ==========================================
        // 【常规军团表现】：渲染和平状态下的军事单位栈
        // ==========================================
        int totalHeadcount = unitsHere.Sum(u => u.Headcount);
        currentToken.GetNode<Label>("Headcount").Text = totalHeadcount.ToString();
        
        var primaryUnit = unitsHere.First();
        var pBarMorale = currentToken.GetNode<ProgressBar>("MoraleProgressBar");
        if (pBarMorale != null) 
        {
            pBarMorale.Value = (float)primaryUnit.Morale / 1000 * 100;
            pBarMorale.Modulate = pBarMorale.Value < 30 ? Colors.Red : Colors.LightBlue;
        }

        var playerInfo = CoreHost.LocalContext.LobbyPlayers.Find(p => p.PlayerId == primaryUnit.OwnerId);
        if (playerInfo != null) currentToken.GetNode<Panel>("Panel").AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = _colorValues[playerInfo.ColorId] });
    }

    // 6. 统一高亮过滤机制：只要当前格子里有任何一个属于我方的单位被框选或选中了，整体 Token 发光高亮
    bool hasSelected = unitsHere.Any(u => _selectedUnitIds.Contains(u.UnitId));
    currentToken.Modulate = hasSelected ? new Color(1.5f, 1.5f, 1.5f, 1.0f) : Colors.White;
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

        // 1. 【新增】：全量预清理，先把所有常规 MilitaryToken 上的行军条藏起来
        foreach (var tokenNode in _stackNodes.Values)
        {
            if (IsInstanceValid(tokenNode) && tokenNode.HasNode("MoveProgressBar"))
            {
                tokenNode.GetNode<ProgressBar>("MoveProgressBar").Visible = false;
            }
        }

        // 2. 仅对当前活跃的行军任务进行高亮和赋值  
        foreach (var kvp in state.ActiveUnitMoves)
        {
            var task = kvp.Value;  
            var unit = state.DeployedUnits.TryGetValue(task.UnitId, out var u) ? u : null;  
            if (unit == null) continue;

            HexCoord coord = unit.CurrentLocation;  
            if (_stackNodes.TryGetValue(coord, out var currentToken) && IsInstanceValid(currentToken))  
            {
                if (currentToken.HasNode("MoveProgressBar"))  
                {
                    var pBar = currentToken.GetNode<ProgressBar>("MoveProgressBar");  
                    pBar.Visible = true; // 仅行军中可见  
                    pBar.MaxValue = task.TotalDaysForNextTile;  
                    pBar.Value = task.TotalDaysForNextTile - task.RemainingDaysForNextTile;  
                }
            }
        }
    }
    
    // ==========================================
    // =====    战斗可视化事件回调函数      =====
    // ==========================================
    
    private void OnCombatStartedSync(WorldSimulationState.CombatSession combat)
    {
        ClientDebugger.LogHandler?.Invoke($"[UI] 聚合战斗爆发，战场坐标: ({combat.Location.X}, {combat.Location.Y}, {combat.Location.Z})");
    
        // 【核心修复】：战斗爆发时，立刻主动刷新该地块的外观，使其无缝从常规 Token 切换为 BattleToken
        UpdateStackVisual(combat.Location);
    }

    private void OnCombatUpdatedSync(WorldSimulationState.CombatSession combat)
    {
        // 战斗每天更新一次，直接调用现有的刷新逻辑
        // 因为 UpdateStackVisual 内部会去读取最新的 Headcount 和 Morale
        UpdateStackVisual(combat.Location);
        
        // 如果面板正开着，刷新一下详细数据
        if (_unitStackPanel.Visible && _currentWatchingCoord == combat.Location)
        {
            ShowUnitStackPanel(_currentWatchingCoord);
        }
    }

// 【修改】：接收到加入 HexCoord 的战斗结束通知
    private void OnCombatEndedSync(int combatId, HexCoord location, int winnerUnitId)
    {
        ClientDebugger.LogHandler?.Invoke($"[UI] 战斗结束，胜利者代表: {winnerUnitId}");
        
        // 【核心修复 Bug 1】：战斗一打完，立刻无缝重绘该地块外观，将 BattleToken 降级销毁并还原为 MilitaryToken
        UpdateStackVisual(location); 
    }
    
    // ==========================================
    // =====    原有的 _Process 和辅助逻辑    =====
    // ==========================================
    public override void _Process(double delta)
    {
        UpdateColonizationFloatingTexts();
        UpdateUnitBuildFloatingTexts(); 
        UpdateOccupationFloatingBars(); // 【新增】每帧驱动占领进度条
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
/// <summary>
    /// 【新增】动态创建、更新和销毁战场地块上的占领进度条
    /// </summary>
    private void UpdateOccupationFloatingBars()
    {
        var state = CoreHost.WorldSimulationState;
        if (state == null) return;

        // 1. 自动清理：如果某个地块的占领任务已经结束（满30天成功或军队撤离），销毁对应的进度条
        var keysToRemove = _occupationProgressBars.Keys.Where(k => !state.ActiveOccupations.ContainsKey(k)).ToList();
        foreach (var key in keysToRemove) 
        { 
            if (IsInstanceValid(_occupationProgressBars[key]))
            {
                _occupationProgressBars[key].QueueFree(); 
            }
            _occupationProgressBars.Remove(key);
        }
        
        // 2. 动态创建与更新：遍历当前所有正在进行的占领任务
        foreach (var kvp in state.ActiveOccupations)
        {
            HexCoord coord = kvp.Key;
            var task = kvp.Value;

            // 如果该地块还没有创建进度条，则动态实例化一个
            if (!_occupationProgressBars.TryGetValue(coord, out ProgressBar pBar) || !IsInstanceValid(pBar))
            {
                pBar = new ProgressBar();
                pBar.CustomMinimumSize = new Vector2(100, 14); // 设定进度条宽高
                pBar.ShowPercentage = false;                 // 隐藏默认的数字百分比，保持画面整洁
                pBar.ZIndex = 100;                           // 确保显示在地图和军队贴图的上层
                
                // 绑定进度条的范围（对应我们设计的 30 天占领规则）
                pBar.MinValue = 0;
                pBar.MaxValue = 30;

                // 计算该六边形在 Godot 瓦片地图中的二维像素坐标
                var offset = MapRenderBridge.CubeToOffset(coord.X, coord.Y, coord.Z);
                Vector2I cellCoords = new Vector2I(offset.offsetX, offset.offsetY);
                
                // 将进度条精确定位到地块的中心偏下方（X轴向左偏移半宽以居中，Y轴向下偏移）
                pBar.Position = _groundLayer.MapToLocal(cellCoords) + new Vector2(-50, 35);
                
                _groundLayer.AddChild(pBar);
                _occupationProgressBars[coord] = pBar;
            }

            // 3. 刷新当前的占领天数进度
            pBar.Value = task.AccumulatedDays;

            // 4. 阵营视觉区分：根据正在占领该地块的主体是谁，赋予不同的高亮颜色
            if (task.OccupyingPlayerId == CoreHost.LocalContext.MyPlayerId)
            {
                pBar.Modulate = Colors.Orange; // 我方正在占领敌方土地，显示橙色/高亮提示
            }
            else
            {
                pBar.Modulate = Colors.Crimson; // 敌方正在占领土地（或中立地），显示深红警告色
            }
        }
    }
    private void OnMonthlySettlementRequiredSync()
    {
        // 月底结算后（包括士气恢复），全量刷新当前大图上所有栈牌的外观
        // 这样就能把加上的士气实时同步到 ProgressBar 表现上 [cite: 1933, 1934]
        foreach (var coord in _stackNodes.Keys.ToList())
        {
            UpdateStackVisual(coord);
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
            CoreHost.WorldSimulationState.OnCombatStarted -= OnCombatStartedSync;
            CoreHost.WorldSimulationState.OnCombatUpdated -= OnCombatUpdatedSync;
            CoreHost.WorldSimulationState.OnCombatEnded -= OnCombatEndedSync;
            CoreHost.WorldSimulationState.OnMonthlySettlementRequired -= OnMonthlySettlementRequiredSync;
            if (_inputHandler != null)
            {
                _inputHandler.OnRightClicked -= OnMapRightClicked;
                _inputHandler.OnBoxSelectedRect -= HandleUnitBoxSelection;
            }
        }
    }
}