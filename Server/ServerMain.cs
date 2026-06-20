using System.Threading;

namespace Server
{
    /// <summary>
    /// 服务端主入口函数
    /// 只负责控制整个服务端主死循环的运行，以及启动/关闭各核心模块。
    /// </summary>
    class ServerMain
    {
        /// <summary> 控制整个服务端主死循环是否运行的开关 </summary>
        private static bool _isRunning = true;

        static void Main(string[] args)
        {
            // 初始化并启动游戏服务端大脑
            GameServer gameServer = new GameServer();
            gameServer.Start();

            // 4. 服务端主线程死循环：低耗轮询网络物理套接字事件
            while (_isRunning)
            {
                // 只有高频调用 PollEvents()，底层物理回调才会被触发
                gameServer.PollEvents();
                Thread.Sleep(15); // 让出 CPU 时间片，防止单核强行占满 100%
            }

            // 5. 善后退出：关闭网络关闭套接字
            gameServer.Stop();
        }
    }
}