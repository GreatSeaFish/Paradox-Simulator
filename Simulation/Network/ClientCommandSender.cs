using FixedMath.NET;
using LiteNetLib;
using LiteNetLib.Utils;
using ParadoxSimulator.Simulation.State;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;
using Shared.Math;
using Shared.Protocol;

namespace ParadoxSimulator.Simulation.Network
{
    /// <summary>
    /// 客户端指令发送器
    /// 职责：高频采集本地玩家的输入（如移动方向），将其打包并通过 LiteNetLib 发送给服务端。
    /// 架构设计：已切换为 Godot 主线程驱动，彻底告别多线程。
    /// </summary>
    public class ClientCommandSender
    {
        // 核心组件依赖：通过网络管理器拿到与服务器的连接（Peer）
        private readonly GameNetworkManager _networkManager;

        private LocalContext _localContext;
        
        // 记录距离上一次发包过去了多少秒。由于移除了独立线程，我们用这个变量在主线程里“数时间”
        private double _packetTimer = 0.0;
        
        // 固定的发包间隔：0.050秒，即50毫秒。换算成频率就是 1s / 0.05s = 20Hz（每秒发20次包）
        private const double PacketInterval = 0.050; 
        
        // 缓存的二进制数据写入器：避免每发一次包都 new 一个新对象，减少 C# 的垃圾回收（GC Alloc）压力
        private readonly NetDataWriter _cachedWriter = new NetDataWriter();

        /// <summary>
        /// 构造函数：在初始化时注入网络管理器
        /// </summary>
        public ClientCommandSender(GameNetworkManager networkManager, LocalContext localContext)
        {
            _networkManager = networkManager;
            _localContext = localContext;
        }


        /// <summary>
        /// ===== 【核心驱动口】由主线程每帧驱动的更新接口 =====
        /// 挂载在 CoreHost 的 _Process 中，每次 Godot 渲染刷新时都会带着 delta 走进来
        /// </summary>
        /// <param name="delta">从 Godot 传进来的单帧增量时间（单位：秒，例如 60帧时约为 0.0166秒）</param>
        public void Update(double delta)
        {
            // 1. 将这一帧消耗的时间叠加到计时器中
            _packetTimer += delta;

            // 2. 检查累计时间是否达到了我们设定的 50ms 发包心跳阈值
            if (_packetTimer >= PacketInterval)
            {
                // 【细节亮点】：减去固定间隔，而不是直接清零(_packetTimer = 0)！
                // 这样能把多出来的余数（比如这一帧走完其实过去了 0.052秒）保留到下一次循环，防止时间精度产生微小漂移。
                _packetTimer -= PacketInterval;

                // 3. 时间触发，执行这一轮的操作采集与物理发包
                ExecuteTickSend();
            }
        }

        /// <summary>
        /// 执行定频采集并发送
        /// </summary>
        private void ExecuteTickSend()
        {
            FixVector2 dir;   // 存储定点数方向
            int playerId;     // 存储本地玩家ID

            // 1. 采集输入：直接读取由渲染层（WorldRender.cs）高频写入的最新本地输入方向
            dir = _localContext.LocalInputDirection;
            // 2. 采集身份：获取本地玩家当前被分配的唯一 ID
            playerId = _localContext.MyPlayerId;

            // 3. 【发包防御】：只有同时满足以下条件才向服务器发送网络包：
            //    - 本地已成功分配到合法的玩家ID (playerId != -1)
            //    - 已经成功连上服务器，物理 Peer 不为空 (_networkManager.ServerPeer != null)
            //    - 【静止沉默机制】：只有玩家方向不为0（按了WASD键）时才发包，松开键盘时保持沉默，大幅压低网络带宽
            if (playerId != -1 && _networkManager.ServerPeer != null && (dir.X != Fix64.Zero || dir.Y != Fix64.Zero))
            {
                // 条件均满足，调用物理发包逻辑
                SendMoveCommandInternal(dir, playerId);
            }
        }


        /// <summary>
        /// 内部物理发包实现：负责组装自定义协议并塞进 LiteNetLib 的底层管道
        /// </summary>
        private void SendMoveCommandInternal(FixVector2 dir, int playerId)
        {
            // 1. 实例化一个专门的玩家指令协议包对象
            var cmd = new PlayerCommand
            {
                PlayerId = playerId,          // 谁发的操作
                InputType = 1,                // 操作类型（1 代表移动）
                MoveDirection = dir           // 具体的定点数方向向量
            };

            // 2. 清空并重置缓存的二进制写入器，准备写入新数据
            _cachedWriter.Reset();
            
            // 3. 【核心步骤】：先写入 1 个字节的包头标识（PacketType.FrameData）
            // 服务器收到网络包时，先 GetByte() 读到这个标识，才知道后面应该用 PlayerCommand 去解析它
            _cachedWriter.Put((byte)PacketType.FrameData);
            
            // 4. 调用协议对象自身的序列化方法，把数据（PlayerId, Seq, 方向的RawValue等）依次写入流中
            cmd.Serialize(_cachedWriter);
            
            // 5. 【物理发送】：通过 LiteNetLib 的 Peer 对象将数据包扔出去
            //    - DeliveryMethod.ReliableOrdered：严格使用【可靠有序】通道。
            //    - 帧同步对指令顺序和完整性要求极高，不能允许任何一个操作指令丢包或者乱序到达服务端。
            _networkManager.ServerPeer!.Send(_cachedWriter, DeliveryMethod.ReliableOrdered);
        }
        
        /// <summary>
        /// 【新增】发送系统时间流速控制指令
        /// </summary>
        public void SendTimeSpeedCommand(int speedLevel)
        {
            int playerId = _localContext.MyPlayerId;
            
            // 安全防御：如果没有合法ID或断网，则不发包
            if (playerId == -1 || _networkManager.ServerPeer == null) return;

            // 1. 组装时间控制专用指令
            var cmd = new PlayerCommand
            {
                PlayerId = playerId,
                InputType = 2,           // 2 代表系统/时间控制
                ActionValue = speedLevel // 具体的时间档位 (0=暂停, 1~5)
            };

            // 2. 清理复用的写入器，写入包头
            _cachedWriter.Reset();
            _cachedWriter.Put((byte)PacketType.FrameData);
            
            // 3. 序列化并发送
            cmd.Serialize(_cachedWriter);
            _networkManager.ServerPeer.Send(_cachedWriter, DeliveryMethod.ReliableOrdered);
            
            ClientDebugger.LogHandler?.Invoke($"[Client] 发送调速请求: {speedLevel}档");
        }
        
        /// <summary>
        /// 【新增】发送地块殖民指令
        /// </summary>
        public void SendColonizeCommand(HexCoord targetHex)
        {
            int playerId = _localContext.MyPlayerId;
            // 安全防御：如果没有合法ID或断网，则不发包
            if (playerId == -1 || _networkManager.ServerPeer == null) return;

            // 1. 组装殖民专用指令
            var cmd = new PlayerCommand
            {
                PlayerId = playerId,
                InputType = 3,           // 3 代表地块殖民指令
                TargetHexX = targetHex.X,
                TargetHexY = targetHex.Y,
                TargetHexZ = targetHex.Z
            };

            // 2. 清理复用的写入器，写入包头
            _cachedWriter.Reset();
            _cachedWriter.Put((byte)PacketType.FrameData);

            // 3. 序列化并发送
            cmd.Serialize(_cachedWriter);
            _networkManager.ServerPeer.Send(_cachedWriter, DeliveryMethod.ReliableOrdered);
            
            ClientDebugger.LogHandler?.Invoke($"[Client] 发起殖民请求: 坐标({targetHex.X}, {targetHex.Y}, {targetHex.Z})");
        }
        
        /// <summary>
        /// 【新增】发送地块造兵指令
        /// </summary>
        public void SendBuildUnitCommand(HexCoord targetHex)
        {
            int playerId = _localContext.MyPlayerId;
            // 安全防御：如果没有合法ID或断网，则不发包
            if (playerId == -1 || _networkManager.ServerPeer == null) return;

            // 1. 组装造兵专用指令
            var cmd = new PlayerCommand
            {
                PlayerId = playerId,
                InputType = 4,           // 4 代表地块造兵指令
                TargetHexX = targetHex.X,
                TargetHexY = targetHex.Y,
                TargetHexZ = targetHex.Z
            };

            // 2. 清理复用的写入器，写入包头
            _cachedWriter.Reset();
            _cachedWriter.Put((byte)PacketType.FrameData);

            // 3. 序列化并发送
            cmd.Serialize(_cachedWriter);
            _networkManager.ServerPeer.Send(_cachedWriter, DeliveryMethod.ReliableOrdered);
            
            ClientDebugger.LogHandler?.Invoke($"[Client] 发起造兵请求: 坐标({targetHex.X}, {targetHex.Y}, {targetHex.Z})");
        }
        
    }
    
}