// using System;
// using System.Threading;
// using ParadoxSimulator.Core;
//
// namespace ParadoxSimulator.Core
// {
//     class CoreConsoleTestMain
//     {
//
//         private static bool _isRunning = true;
//
//         public static void Main(string[] args)
//         {
//             ClientDebuger.LogHandler?.Invoke($"=== 帧同步客户端启动 ===");
//
//             // 1. 初始化网络管理器并连接
//             GameNetworkManager networkManager = new GameNetworkManager();
//             networkManager.Initialize();
//             networkManager.Connect("127.0.0.1", 5721, "FrameSyncDemo");
//
//             // 2. 初始化并启动指令同步器
//             ClientCommandSender commandSynchronizer = new ClientCommandSender(networkManager);
//             commandSynchronizer.Start();
//
//             // 3. 初始化并启动回合逻辑驱动中心
//             GameTurnManager turnManager = new GameTurnManager();
//             turnManager.Start();
//
//             // 4. 主线程事件轮询死循环
//             while (_isRunning)
//             {
//                 networkManager.PollEvents();
//                 Thread.Sleep(15);
//             }
//
//             // 5. 善后退出
//             commandSynchronizer.Stop();
//             turnManager.Stop();
//             networkManager.Stop();
//         }
//     }
// }