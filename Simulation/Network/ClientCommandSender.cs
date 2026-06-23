using System.Collections.Concurrent;
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
    /// 客户端指令发送器 (已解耦优化)
    /// 职责：维护发送队列，定频采样连续操作，统一封包发送。
    /// </summary>
    public class ClientCommandSender
    {
        private readonly GameNetworkManager _networkManager;
        private LocalContext _localContext;
        
        private double _packetTimer = 0.0;
        private const double PacketInterval = 0.050;
        private readonly NetDataWriter _cachedWriter = new NetDataWriter();

        // 【新增】：离散指令缓冲区。UI 或 Input 系统产生的离散操作先存放在这里
        private readonly ConcurrentQueue<PlayerCommand> _pendingCommands = new();

        public ClientCommandSender(GameNetworkManager networkManager, LocalContext localContext)
        {
            _networkManager = networkManager;
            _localContext = localContext;
        }

        /// <summary>
        /// 【对外暴露的通用发送接口】
        /// 任何 UI 或交互系统想发指令，只需组装好 PlayerCommand 丢进队列即可。
        /// ClientCommandSender 彻底与“造兵/殖民”等具体业务解耦。
        /// </summary>
        public void EnqueueCommand(PlayerCommand cmd)
        {
            _pendingCommands.Enqueue(cmd);
        }

        public void Update(double delta)
        {
            _packetTimer += delta;

            if (_packetTimer >= PacketInterval)
            {
                _packetTimer -= PacketInterval;
                ExecuteTickSend();
            }
        }

        private void ExecuteTickSend()
        {
            int playerId = _localContext.MyPlayerId;
            
            // 安全防御
            if (playerId == -1 || _networkManager.ServerPeer == null) return;

            // 1. 处理所有积压的【离散型指令】(点击、建造、系统操作等)
            while (_pendingCommands.TryDequeue(out var cmd))
            {
                cmd.PlayerId = playerId; // 补全身份标识
                SendInternal(cmd);
            }

            // 2. 采样并发送【连续型指令】(移动)
            // 只有摇杆有输入时才发送，保持静默优化
            FixVector2 dir = _localContext.LocalInputDirection;
            if (dir.X != Fix64.Zero || dir.Y != Fix64.Zero)
            {
                var moveCmd = new PlayerCommand
                {
                    PlayerId = playerId,
                    InputType = CommandType.Move, // 修复点：使用枚举
                    MoveDirection = dir
                };
                SendInternal(moveCmd);
            }
        }

        /// <summary>
        /// 底层物理发包方法保持极简
        /// </summary>
        private void SendInternal(PlayerCommand cmd)
        {
            _cachedWriter.Reset();
            _cachedWriter.Put((byte)PacketType.FrameData); 
            cmd.Serialize(_cachedWriter); 
            _networkManager.ServerPeer!.Send(_cachedWriter, DeliveryMethod.ReliableOrdered);
        }
    }
}