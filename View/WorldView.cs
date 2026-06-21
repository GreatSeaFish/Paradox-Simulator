using Godot;
using System.Collections.Generic;
using FixedMath.NET;
using Shared.Math;

public partial class WorldView : Node
{
    private Dictionary<int, ColorRect> _playerNodes = new(); 
    // 仅保留当前最新的位置容器，移除 _oldPositions
    private readonly Dictionary<int, FixVector2> _currentPositions = new();

    // 预设颜色（必须与大厅的 _colorValues 保持一致）
    private readonly Color[] _colorValues = { 
        Colors.Red, Colors.Blue, Colors.Orange, Colors.Green, 
        Colors.Yellow, Colors.Purple, Colors.Pink, Colors.LightBlue 
    }; 

    public override void _Ready()
    {
        
        // [新增] 2. 通知地图渲染节点，将分配好的领地颜色画出来
        var mapRender = GetNode<WorldMapRender>("WorldMapRender");
        mapRender.UpdateOwnershipVisuals();
        
        // 2. 根据大厅名单动态生成 Godot 渲染节点
        foreach (var player in CoreHost.LocalContext.LobbyPlayers) 
        {
            var rect = new Godot.ColorRect
            {
                Size = new Vector2(30f, 30f), // 初始即设定为 30x30 大小，避免在 _Process 中重复赋值 
                Color = _colorValues[player.ColorId] 
            }; 

            // 添加到场景树中
            AddChild(rect); 
            _playerNodes[player.PlayerId] = rect; 

            // 预先填充位置字典的 Key
            _currentPositions[player.PlayerId] = FixVector2.Zero;
        }
    }

    public override void _Process(double delta)
    {
        // ==================== 1. 采集输入与发包 ====================
        Vector2 godotDir = Input.GetVector("a", "d", "w", "s"); 
        godotDir.Y = -godotDir.Y; // 保持物理世界：上为正，下为负 
    
        if (godotDir != Vector2.Zero)
        {
            godotDir = godotDir.Normalized(); 
            CoreHost.LocalContext.SetLocalInput(new FixVector2((Fix64)godotDir.X, (Fix64)godotDir.Y)); 
        }
        else
        {
            CoreHost.LocalContext.SetLocalInput(FixVector2.Zero); 
        }

        // ==================== 2. 移除平滑插值，直接渲染 ====================
        // 直接抓取当前最新的绝对逻辑坐标
        CoreHost.WorldSimulationState.GetLogicalPositions(_currentPositions);

        float worldScale = 50f; // 缩放因子：1个逻辑单位 = 50个屏幕像素 
        Vector2 screenCenter = new Vector2(500f, 400f); // 屏幕中心偏移 

        foreach (var pair in _playerNodes) 
        {
            int playerId = pair.Key; 
            ColorRect rectNode = pair.Value; 

            if (IsInstanceValid(rectNode) && _currentPositions.ContainsKey(playerId)) 
            {
                FixVector2 currentFix = _currentPositions[playerId];

                // 彻底抛弃 Lerp 混合，直接由定点数映射到像素屏幕坐标
                float screenX = screenCenter.X + ((float)currentFix.X * worldScale); 
                float screenY = screenCenter.Y - ((float)currentFix.Y * worldScale); 

                rectNode.Position = new Vector2(screenX - 15f, screenY - 15f); 
            }
        }
    }
}