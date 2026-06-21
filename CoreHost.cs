using Godot;
using System;
using ParadoxSimulator.Core;
using ParadoxSimulator.Core.GameData;
using ParadoxSimulator.Core.WorldMapSystem;

public partial class CoreHost : Node
{
    // ================== 数据仓库 (Data) ==================
    public static LocalContext LocalContext { get; private set; } = null!;
    public static WorldSimulationState WorldSimulationState { get; private set; } = null!;
    public static MapConfig MapConfig { get; private set; } = null!;
    public static GameState GameState { get; private set; } = null!;

    // ================== 系统管理器 (Systems) ==================
    public static GameNetworkManager NetworkManager { get; private set; } = null!;
    public static ClientCommandSender CommandSender { get; private set; } = null!;
    public static ServerCommandHandler CommandHandler { get; private set; } = null!;
    public static TimeSystem TimeSystem { get; private set; } = null!;
    public static MapLoader MapLoader { get; private set; } = null!;
    public static SettlementSystem SettlementSystem { get; private set; } = null!;
    public static GameStateSystem StateSystem { get; private set; } = null!;
    
    
    
    public override void _Ready()
    {
        GD.Print("[CoreLauncher] 正在初始化核心网络与逻辑模块...");
        ClientDebugger.LogHandler = (msg) => GD.Print(msg);
        ClientDebugger.WarningHandler = (msg) => GD.PrintRich($"[color=yellow]{msg}[/color]");
        
        
        GameState = new GameState();
        LocalContext = new LocalContext();
        WorldSimulationState = new WorldSimulationState();
        MapConfig = new MapConfig();

        // 1. 仅做实例化和本地初始化，不直接进行网络连接
        NetworkManager = new GameNetworkManager(LocalContext);
        NetworkManager.Initialize();
        
        SettlementSystem = new SettlementSystem();
        TimeSystem = new TimeSystem(SettlementSystem, WorldSimulationState);
        
        CommandSender = new ClientCommandSender(NetworkManager, LocalContext);
        CommandHandler = new ServerCommandHandler(TimeSystem, LocalContext, WorldSimulationState);
        // 【新增】实例化状态管理器
        StateSystem = new GameStateSystem(GameState, WorldSimulationState, LocalContext);
        
        // 【新增】绑定开局事件：当网络层收到开局包时，立刻在逻辑层完成状态切换和数据准备
        NetworkManager.OnGameStartReceived += () => 
        {
            StateSystem.StartGame();
        };
        
        MapLoader = new MapLoader(MapConfig, WorldSimulationState);
        MapLoader.LoadMapData("Maps/terrain_data.json");
        
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