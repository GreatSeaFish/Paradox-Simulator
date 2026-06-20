using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using LiteNetLib;
using LiteNetLib.Utils;
using Shared.Protocol;

namespace Server
{
    /// <summary>
    /// 职责：
    /// 1. 负责游戏开始后的定频“逻辑帧”心跳广播（20Hz / 50ms 一帧）。
    /// 2. 收集所有客户端的操作指令，打包成标准帧并统一广播，确保所有客户端状态完全一致。
    /// </summary>
    public class FrameSyncEngine
    {
        // ==========================================
        // ===== 帧同步核心数据结构 =====
        // ==========================================
        
        /// <summary> 
        /// 玩家输入指令缓冲区。
        /// 采用多线程安全的并发队列（ConcurrentQueue），因为网络接收线程在往里写，而定时帧心跳线程在从中读。
        /// </summary>
        private readonly ConcurrentQueue<PlayerCommand> _inputBuffer = new();
        
        /// <summary> 
        /// 逻辑帧历史记录字典（Key: FrameId, Value: 帧数据包）。
        /// 用于后续可能实现的“断线重连补帧”机制或游戏回放系统。
        /// </summary>
        private readonly Dictionary<int, FramePacket> _frameHistory = new();
        
        // ==========================================
        // ===== 状态控制变量 =====
        // ==========================================
        
        /// <summary> 游戏开局后，严格递增的全局逻辑帧号。从第一帧开始推进 </summary>
        private int _currentFrameId = 1;

        /// <summary> 控制引擎循环是否运行的开关 </summary>
        private bool _isRunning = true;
        
        /// <summary> 游戏正式开局标记。若为 true，则全力转入游戏内高频发包状态 </summary>
        public bool IsGameStarted { get; set; } = false;

        private NetManager _netManager = null!;

        public void Init(NetManager netManager)
        {
            _netManager = netManager;
        }

        public void HandleFrameData(NetPeer fromPeer, NetDataReader dataReader)
        {
            // 【安全拦截】：只有当所有人都准备就绪、游戏正式打上开局标记后，才接收处理逻辑帧操作指令
            if (IsGameStarted)
            {
                var command = new PlayerCommand();
                command.Deserialize(dataReader); // 反序列化出客户端上传的移动/调速操作
                command.PlayerId = fromPeer.Id;  // 【防伪造防作弊】：强制覆盖为底层的物理 PeerID，保证安全
                
                // 将洗净的操作指令丢入并发输入缓冲区，等待 TickLoop 线程将其定时打包广播
                _inputBuffer.Enqueue(command);
            }
        }

        public void StartTickLoop()
        {
            _isRunning = true;
            // 独立逻辑线程：启动高精度定时器（帧同步心脏循环）
            // 此线程不受 Main 线程死循环影响，完全独立高频运转
            Thread tickThread = new Thread(ServerTickLoop) { IsBackground = true };
            tickThread.Start();
        }

        public void Stop()
        {
            _isRunning = false;
        }

        /// <summary>
        /// 【物理心脏循环】：独立的高精度定频心跳驱动死循环（运行在次线程）
        /// 基于 Stopwatch（微秒级硬件时间戳）强力杜绝常规 Thread.Sleep 产生的时间漂移问题
        /// </summary>
        private void ServerTickLoop()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            
            // 预设下一帧到期的标准物理时间点（50毫秒后）
            long nextTickTime = sw.ElapsedMilliseconds + 50;

            while (_isRunning)
            {
                long currentTime = sw.ElapsedMilliseconds;

                // 只有当当前硬件时间超过了预设的终点，才说明足足过去了 50ms 
                if (currentTime >= nextTickTime)
                {
                    // 只有开局后，心跳才真正产生逻辑帧包并派发
                    if (IsGameStarted)
                    {
                        UpdateFrame();
                    }
                    nextTickTime += 50; // 时间点雷打不动严格向后递增 50ms，彻底烫平单次垃圾回收(GC)引起的波动
                }
                Thread.Sleep(1); // 极其轻微的挂起，确保逻辑不吃满单核 CPU 
            }
        }

        /// <summary>
        /// 帧同步逻辑打包核心方法：收割这段时间积攒的指令，凝聚为一个固定的逻辑帧
        /// </summary>
        private void UpdateFrame()
        {
            // 1. 实例化标准的一个物理包对象，并印上专属的唯一逻辑帧 ID 号
            var packet = new FramePacket { FrameId = _currentFrameId };

            // 2. 核心收割：将这 50ms 期间所有网络线程塞进 _inputBuffer 并发队列里的玩家指令，
            // 全部一个个弹出来，收集装填到本帧的 Commands 集合中。
            // 【细节】：如果玩家这 50ms 没动、没发指令，Commands 就为空（也就是俗称的“空帧”，必须要发以驱动时钟）
            while (_inputBuffer.TryDequeue(out var cmd))
            {
                packet.Commands.Add(cmd);
            }

            // 3. 将组装好的这一帧永久封存进历史字典，备查
            _frameHistory[_currentFrameId] = packet;

            // 4. 发送广播：只有有物理对端连接时才派发
            if (_netManager != null && _netManager.ConnectedPeersCount > 0)
            {
                NetDataWriter writer = new NetDataWriter();
                writer.Put((byte)PacketType.FrameData); // 【核心契约】：写入帧同步逻辑数据包头标签 6
                packet.Serialize(writer);
                
                // 采用可靠有序（ReliableOrdered）方式群发。
                // 帧同步的绝对真理：绝对不允许丢弃任何一个中间帧，也绝对不允许由于乱序先跑第三帧、再跑第二帧。
                _netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
            }

            // 5. 帧号庄严向前跨越一步，为 50ms 后的下一轮打包做好准备
            _currentFrameId++;
        }
    }
}