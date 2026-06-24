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
    private readonly Dictionary<int, Node2D> _spawnedUnitNodes = new(); // Key 从 HexCoord 改为 UnitId
    private readonly List<int> _selectedUnitIds = new();                // 当前选中的部队 ID 列表
    
    

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
        _unitsRoot = GetNode<Node2D>("Units");

        // C. 通过注入必要的依赖完成子组件初始化
        var selectMark = GetNode<Sprite2D>("WorldMapRender/MapInputHandler/SelectMark");
        var selectionBox = GetNode<ColorRect>("Hud/Root/SelectionBox");
        _inputHandler.Init(_groundLayer, selectMark, selectionBox);
        _territoryVisualizer.Init(GetNode<Node2D>("WorldMapRender/OwnershipFilter/TerritorialMarkLayers"));

        // D. 桥接横向组件的信号（让输入处理器直接单向驱动属性面板）
        _inputHandler.OnTileSelected += (coord, tileData) => {
            _inspectorPanel.Inspect(coord, tileData);
            // 【修复】：移除了 HandleUnitSelection，现在点击地块不再自动选中部队
        };
        _inputHandler.OnTileDeselected += () => {
            _inspectorPanel.Visible = false;
        };
        // 面板点击关闭按钮时，反向通知输入处理器清理地图高亮标记
        _inspectorPanel.OnCloseRequested += () => {
            _inspectorPanel.Visible = false;
            _inputHandler.ClearSelection();
        };

        // E. 监听右键和框选（由输入处理器派发）
        _inputHandler.OnRightClicked += OnMapRightClicked;
        // 【修改】：使用新的真实物理矩形框选事件
        _inputHandler.OnBoxSelectedRect += HandleUnitBoxSelection;

        // F. 统一且唯一地绑定全局逻辑状态事件，彻底消除重复订阅导致的“幽灵兵”
        if (CoreHost.WorldSimulationState != null)
        {
            CoreHost.WorldSimulationState.OnTileOwnershipChanged += OnTileOwnershipChangedSync;
            CoreHost.WorldSimulationState.OnUnitSpawned += OnUnitSpawnedSync;
            CoreHost.WorldSimulationState.OnUnitRemoved += OnUnitRemovedSync;
            CoreHost.WorldSimulationState.OnUnitStepped += OnUnitSteppedSync;
            CoreHost.WorldSimulationState.OnSpeedChanged += OnSpeedChangedSync;
            CoreHost.WorldSimulationState.OnDateChanged += OnDateChangedSync;
        }

        // G. 驱动虚拟时间与日历系统绑定
        _timeFlowRateTab.TabClicked += OnTimeTabClicked;
        if (CoreHost.TimeSystem != null)
        {
            UpdateCalendarText(CoreHost.WorldSimulationState.CurrentDate);
        }
        _timeFlowRateTab.CurrentTab = 0;

        // H. 局内首帧画面渲染对齐
        _territoryVisualizer.UpdateOwnershipVisuals();
        
        // （如果你已经不需要早期测试用的纯色方块光标了，可以直接删掉这行）
        CreatePlayerNodes(); 
    }
    
    
    private void HandleSingleUnitSelection(int unitId)
    {
        ClearUnitSelection(); // 先清空其他高亮
        _selectedUnitIds.Add(unitId);
    
        if (_spawnedUnitNodes.TryGetValue(unitId, out var token) && IsInstanceValid(token))
        {
            // 选中后提亮兵牌作为视觉反馈
            token.Modulate = new Color(1.5f, 1.5f, 1.5f, 1.0f);
        }
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
        UpdateUnitMoveProgressBars();
    }

    // private void OnTimeTabClicked(long tabIndex)
    // {
    //     CoreHost.CommandSender.SendTimeSpeedCommand((int)tabIndex);
    // }
    
    private void OnTimeTabClicked(long tabIndex)
    {
        // 使用统一的入队接口，传入包装好的枚举和参数
        CoreHost.CommandSender.EnqueueCommand(new PlayerCommand
        {
            InputType = CommandType.TimeSpeedControl,
            ActionValue = (int)tabIndex
        });
    }

    private void OnSpeedChangedSync(int newSpeedLevel)
    {
        if (_timeFlowRateTab.CurrentTab != newSpeedLevel)
        {
            _timeFlowRateTab.CurrentTab = newSpeedLevel; 
        }
    }

    private void OnDateChangedSync(GameDateTime newDate)
    {
        UpdateCalendarText(newDate); 
    }

    private void UpdateCalendarText(GameDateTime date)
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
    /// 处理部队的单选或框选
    /// </summary>
    private void HandleUnitSelection(List<HexCoord> hexes)
    {
        ClearUnitSelection(); // 先清空上次选中的高亮

        var state = CoreHost.WorldSimulationState;
        int myId = CoreHost.LocalContext.MyPlayerId;
    
        foreach (var coord in hexes)
        {
            // 找出这个格子上所有属于我的部队
            var unitsHere = state.DeployedUnits.Values
                .Where(u => u.CurrentLocation == coord && u.OwnerId == myId);

            foreach (var unit in unitsHere)
            {
                _selectedUnitIds.Add(unit.UnitId);
            
                // 兵牌高亮反馈 (通过 UnitId 在渲染字典中找预制体)
                if (_spawnedUnitNodes.TryGetValue(unit.UnitId, out var token) && IsInstanceValid(token))
                {
                    token.Modulate = new Color(1.5f, 1.5f, 1.5f, 1.0f);
                }
            }
        }
    }
    /// <summary>
    /// 清空选中的部队并恢复外观
    /// </summary>
    private void ClearUnitSelection()
    {
        foreach (var unitId in _selectedUnitIds)
        {
            if (_spawnedUnitNodes.TryGetValue(unitId, out var token) && IsInstanceValid(token))
            {
                token.Modulate = Colors.White; // 恢复原色
            }
        }
        _selectedUnitIds.Clear();
    }

    /// <summary>
    /// 处理右键点击目标地块：下达移动指令！
    /// </summary>
    private void OnMapRightClicked(HexCoord targetHex)
    {
        if (_selectedUnitIds.Count == 0) return;
        var state = CoreHost.WorldSimulationState;

        foreach (var unitId in _selectedUnitIds)
        {
            if (!state.DeployedUnits.TryGetValue(unitId, out var unit)) continue;

            // 防御：源地址和目标地址不能相同
            if (unit.CurrentLocation == targetHex) continue;

            CoreHost.CommandSender.EnqueueCommand(new PlayerCommand
            {
                InputType = CommandType.UnitMove,
                ActionValue = unitId, // 【核心修改】：精准传递兵牌的实体ID，不再发起点坐标了
                TargetHexX = (short)targetHex.X,
                TargetHexY = (short)targetHex.Y,
                TargetHexZ = (short)targetHex.Z
            });
        }
    
        ClearUnitSelection();
    }

    /// <summary>
    /// 响应逻辑层：部队行军开始或被消灭，销毁兵牌模型
    /// </summary>
    // 方法参数改为 int unitId
    private void OnUnitRemovedSync(int unitId)
    {
        if (_spawnedUnitNodes.TryGetValue(unitId, out var tokenNode))
        {
            if (IsInstanceValid(tokenNode)) tokenNode.QueueFree();
            _spawnedUnitNodes.Remove(unitId);
        }
        _selectedUnitIds.Remove(unitId); // 如果它原本在选中列表里，安全移除
    }
    /// <summary>
    /// 【新增】接收到逻辑层的部署指令后，在场景中真正生成预制体
    /// </summary>
    private void OnUnitSpawnedSync(WorldSimulationState.MilitaryUnit unit)
    {
        if (_militaryTokenScene.Instantiate() is Node2D tokenNode)
        {
            // 1. 添加到场景树
            _unitsRoot.AddChild(tokenNode);
        
            // 2. 绝对唯一绑定：使用 UnitId 作为 Key，彻底杜绝原地克隆的幽灵 Bug
            _spawnedUnitNodes[unit.UnitId] = tokenNode; 

            // 3. 计算该坐标在 Godot 2D 世界中的基础像素位置
            var offset = MapRenderBridge.CubeToOffset(unit.CurrentLocation.X, unit.CurrentLocation.Y, unit.CurrentLocation.Z);
            Vector2I cellCoords = new Vector2I(offset.offsetX, offset.offsetY);
    
            // 4. 设置位置：加上堆叠偏移量（同格子的多支部队会向右上方稍微错开堆叠）
            tokenNode.Position = _groundLayer.MapToLocal(cellCoords) + GetStackOffset(unit.CurrentLocation, unit.UnitId);
        
            // 5. 赋值兵力数字：直接从传入的 unit 实体中读取 Headcount
            var headcountLabel = tokenNode.GetNode<Label>("Headcount");
            if (headcountLabel != null) headcountLabel.Text = unit.Headcount.ToString();

            // 6. 为底板染色，区分玩家阵营
            var panel = tokenNode.GetNode<Panel>("Panel");
            if (panel != null)
            {
                // 直接从传入的 unit 实体中读取 OwnerId 去匹配玩家信息
                var playerInfo = CoreHost.LocalContext.LobbyPlayers.Find(p => p.PlayerId == unit.OwnerId);
                if (playerInfo != null)
                {
                    // 给 Panel 挂载动态着色方案
                    var styleBox = new StyleBoxFlat { BgColor = _colorValues[playerInfo.ColorId] };
                    panel.AddThemeStyleboxOverride("panel", styleBox);
                }
            }
// 【新增】：监听兵牌本身的点击事件
            panel.GuiInput += (@event) => 
            {
                if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
                {
                    // 只选中当前这支被点击的部队
                    HandleSingleUnitSelection(unit.UnitId);
                    
                    // 【核心】：吞掉这个事件，阻止它穿透到背后的地块网格！
                    panel.AcceptEvent(); 
                }
            };
            // 7. 确保刚生成时，行军进度条是归零状态的
            var pBar = tokenNode.GetNode<ProgressBar>("MoveProgressBar");
            if (pBar != null) pBar.Value = 0; 
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
            CoreHost.WorldSimulationState.OnUnitStepped -= OnUnitSteppedSync;
            CoreHost.WorldSimulationState.OnSpeedChanged -= OnSpeedChangedSync;
            CoreHost.WorldSimulationState.OnDateChanged -= OnDateChangedSync; 
            // 【新增】注销事件防止内存泄漏
            CoreHost.WorldSimulationState.OnTileOwnershipChanged -= OnTileOwnershipChangedSync;
            // 【新增】注销事件防止内存泄漏
            CoreHost.WorldSimulationState.OnTileOwnershipChanged -= OnTileOwnershipChangedSync;
            CoreHost.WorldSimulationState.OnUnitSpawned -= OnUnitSpawnedSync;
            // 【新增】
            if (_inputHandler != null)
            {
                _inputHandler.OnRightClicked -= OnMapRightClicked;
                _inputHandler.OnBoxSelectedHexes -= HandleUnitSelection;
            }
        }
    }
    
    private void UpdateUnitMoveProgressBars()
    {
        var state = CoreHost.WorldSimulationState;
        if (state == null) return;

        // 1. 首先把全场所有兵牌的移动进度条默认清零/隐藏
        foreach (var nodeKvp in _spawnedUnitNodes)
        {
            if (IsInstanceValid(nodeKvp.Value))
            {
                var pBar = nodeKvp.Value.GetNode<ProgressBar>("MoveProgressBar");
                if (pBar != null) pBar.Value = 0; 
            }
        }

        // 2. 遍历当前所有活跃的行军任务，动态计算并填满对应的进度条
        foreach (var kvp in state.ActiveUnitMoves)
        {
            var task = kvp.Value;
    
            // 【核心修复】：直接靠部队唯一的 UnitId，在 UI 层精准定位到这个兵牌实体
            if (_spawnedUnitNodes.TryGetValue(task.UnitId, out var tokenNode))
            {
                if (IsInstanceValid(tokenNode))
                {
                    var pBar = tokenNode.GetNode<ProgressBar>("MoveProgressBar");
                    if (pBar != null && task.TotalDaysForNextTile > 0)
                    {
                        // 计算当前这一格的移动百分比：(1 - 剩余天数 / 总天数) * 100
                        float progress = (1.0f - (float)task.RemainingDaysForNextTile / task.TotalDaysForNextTile) * 100f;
                        pBar.Value = progress;
                    }
                }
            }
        }
    }
    
    // 确保在 _Ready() 中绑定了该事件：
// CoreHost.WorldSimulationState.OnUnitStepped += OnUnitSteppedSync;

    // 3. 接收平移事件 (瞬间移动 + 重新计算堆叠偏移)
    private void OnUnitSteppedSync(int unitId, HexCoord from, HexCoord to)
    {
        if (_spawnedUnitNodes.TryGetValue(unitId, out var tokenNode) && IsInstanceValid(tokenNode))
        {
            var offset = MapRenderBridge.CubeToOffset(to.X, to.Y, to.Z);
            Vector2I cellCoords = new Vector2I(offset.offsetX, offset.offsetY);
        
            // 瞬间移动，并加上多部队堆叠避让偏移
            tokenNode.Position = _groundLayer.MapToLocal(cellCoords) + GetStackOffset(to, unitId);
        
            var pBar = tokenNode.GetNode<ProgressBar>("MoraleProgressBar");
            if (pBar != null) pBar.Value = 0;
        }
    }
    
    // 4. 新增的视觉堆叠算法（避免同一格的兵完全重合）
    private Vector2 GetStackOffset(HexCoord coord, int unitId)
    {
        var state = CoreHost.WorldSimulationState;
        if (state == null) return Vector2.Zero;

        // 找出这个格子上所有的部队并按 ID 排序
        var unitsHere = state.DeployedUnits.Values
            .Where(u => u.CurrentLocation == coord)
            .OrderBy(u => u.UnitId).ToList();

        int index = unitsHere.FindIndex(u => u.UnitId == unitId);
        if (index <= 0) return Vector2.Zero; // 第一个兵在正中心

        // 第二个兵及以后，向右上方微微错开堆叠
        return new Vector2(index * 12f, index * -12f); 
    }
    /// <summary>
    /// 【新增】处理真实物理坐标的框选，完美解决堆叠偏移导致的漏选问题
    /// </summary>
    private void HandleUnitBoxSelection(Rect2 selectionRect)
    {
        ClearUnitSelection(); // 先清空上次选中的高亮

        var state = CoreHost.WorldSimulationState;
        int myId = CoreHost.LocalContext.MyPlayerId;

        // 直接遍历当前场景中渲染出来的兵牌实体
        foreach (var kvp in _spawnedUnitNodes)
        {
            int unitId = kvp.Key;
            Node2D tokenNode = kvp.Value;

            if (!IsInstanceValid(tokenNode)) continue;

            // 必须是我自己的部队
            if (state.DeployedUnits.TryGetValue(unitId, out var unit) && unit.OwnerId == myId)
            {
                // 【核心判定】：兵牌当前的真实世界坐标（包含堆叠偏移后的位置）是否在框选的矩形内！
                if (selectionRect.HasPoint(tokenNode.GlobalPosition))
                {
                    _selectedUnitIds.Add(unitId);
                    tokenNode.Modulate = new Color(1.5f, 1.5f, 1.5f, 1.0f); // 提亮高亮
                }
            }
        }
    }
}

