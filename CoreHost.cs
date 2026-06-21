using Godot;
using System;
using ParadoxSimulator.Core;
using ParadoxSimulator.Core.WorldMapSystem;

public partial class CoreHost : Node
{
    public static GameNetworkManager NetworkManager { get; private set; } = null!;
    public static ClientCommandSender CommandSender { get; private set; } = null!;
    public static ServerCommandHandler CommandHandler { get; private set; } = null!;
    public static GameTime GameTime { get; private set; } = null!;
    public static SettlementManager SettlementManager { get; private set; } = null!;
    // 【新增】全局共享的地图数据中心
    public static MapData MapData { get; private set; } = null!;
    
    
    public override void _Ready()
    {
        GD.Print("[CoreLauncher] 正在初始化核心网络与逻辑模块...");
        
        ClientDebuger.LogHandler = (msg) => GD.Print(msg);
        ClientDebuger.WarningHandler = (msg) => GD.PrintRich($"[color=yellow]{msg}[/color]");

        // 1. 仅做实例化和本地初始化，不直接进行网络连接
        NetworkManager = new GameNetworkManager();
        NetworkManager.Initialize();
        
        SettlementManager = new SettlementManager();
        GameTime = new GameTime(SettlementManager);
        
        CommandSender = new ClientCommandSender(NetworkManager);
        CommandHandler = new ServerCommandHandler(GameTime);
        
        // 2. 【新增】初始化地图数据
        MapData = new MapData();
        string projectRoot = ProjectSettings.GlobalizePath("res://");
        string jsonPath = System.IO.Path.Combine(projectRoot, "Core", "WorldMapSystem", "Maps", "terrain_data.json");
        MapData.LoadMapData(jsonPath);
        
    }
    
    // 2. 新增：供 PageLogin 调用的公开连接接口
    public static void ConnectToServer(string serverIp, int serverPort)
    {
        if (NetworkManager == null || CommandSender == null || CommandHandler == null)
        {
            GD.PrintErr("[CoreHost] 组件未初始化完毕，无法连接！");
            return;
        }


        // 真正开始连接服务器
        NetworkManager.Connect(serverIp, serverPort, "FrameSyncDemo");
        GD.Print($"[CoreHost] 正在连接服务器 {serverIp}:{serverPort}...");
    }
    
    public override void _Process(double delta)
    {
        // 保持每帧轮询
        NetworkManager?.PollEvents();
        
        // 2. 驱动指令发送器（50ms 定频输入采集发包）
        CommandSender?.Update(delta);

        // 3. 驱动逻辑帧步进（将 delta 喂给状态机进行 50ms 定频更新/追帧）
        CommandHandler?.Update(delta);
    }
}