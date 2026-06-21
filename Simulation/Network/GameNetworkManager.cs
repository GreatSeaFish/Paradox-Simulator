using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using ParadoxSimulator.Core.GameData;
using Shared.Protocol;

namespace ParadoxSimulator.Core
{
    /// <summary>
    /// 客户端网络管理器
    /// 职责：负责底层的物理连接管理、网络事件轮询、自定义二进制协议的打包发送与解包分发中心。
    /// </summary>
    public class GameNetworkManager(LocalContext localContext)
    {
        // LiteNetLib 核心组件：负责管理物理连接、网络吞吐和线程安全
        private NetManager _client = null!;
        // LiteNetLib 事件监听器：基于事件驱动（如连接成功、收到网络包等物理回调）
        private EventBasedNetListener _listener = null!;
        // 与服务端的连接对端实例：后续所有的物理数据发送都必须通过这个 Peer 扔出去
        private NetPeer? _serverPeer;

        /// <summary>
        /// 公开属性：获取当前连接的服务端 Peer
        /// </summary>
        public NetPeer? ServerPeer => _serverPeer;

        // ==========================================
        // ===== 【解耦设计】UI层专用的网络事件 =====
        // ==========================================
        
        /// <summary>
        /// 当成功连接服务器并被分配玩家ID时触发（供登录/大厅UI挂载）
        /// </summary>
        public event Action<int>? OnInitReceived;
        
        /// <summary>
        /// 当大厅玩家名单、准备状态发生变动，收到服务器同步数据时触发（供大厅列表刷新UI挂载）
        /// </summary>
        public event Action<List<LobbyPlayerInfo>>? OnLobbySyncReceived;
        
        /// <summary>
        /// 当所有人都准备就绪，收到服务器下发的正式开局通知时触发（供场景切换挂载）
        /// </summary>
        public event Action? OnGameStartReceived;

        /// <summary>
        /// 网络层本地初始化：组装网络监听事件并建立数据分发路由表
        /// </summary>
        public void Initialize()
        {
            // 1. 实例化监听器与管理器
            _listener = new EventBasedNetListener();
            _client = new NetManager(_listener);
            
            // 2. 注册物理连接成功回调
            _listener.PeerConnectedEvent += peer =>
            {
               ClientDebugger.LogHandler?.Invoke($"[Client] 已成功连接服务器，等待服务器分配 ID...");
            };

            // 3. 【网络核心】注册消息接收回调：只要服务器发包过来，底层的网络管道就会把二进制流带进这个 Lambda 闭包中
            _listener.NetworkReceiveEvent += (fromPeer, dataReader, channel, deliveryMethod) =>
            {
                // 【包头防御机制】：首先且必须只读取 1 个 Byte 的包头数据
                // 这一步决定了后面的二进制流究竟是代表什么业务数据，防止解析错位
                PacketType packetType = (PacketType)dataReader.GetByte();

                // 根据包头标签，进入各自独立的反序列化解包与分发业务逻辑
                switch (packetType)
                {
                    case PacketType.ServerInit:
                        // A. 实例化对应的初始化协议包对象
                        var initPacket = new InitPacket();
                        // B. 将剩余的二进制流反序列化，填充到对象字段中
                        initPacket.Deserialize(dataReader);
                        
                        // C. 写入静态客户端全局缓存：记录我在这局游戏中的唯一身份 ID
                        localContext.MyPlayerId = initPacket.AssignedPlayerId;
                        ClientDebugger.LogHandler?.Invoke($"[Client] 收到开局分配ID: {localContext.MyPlayerId}");
                        
                        // D. 【事件分发】：向外抛出事件，通知挂载了此事件的UI页面（如隐藏登录中，提示连接大厅）
                        OnInitReceived?.Invoke(initPacket.AssignedPlayerId);
                        break;

                    case PacketType.ServerLobbySync:
                        // A. 实例化大厅人员同步协议包对象
                        var syncPacket = new ServerLobbySyncPacket();
                        syncPacket.Deserialize(dataReader);
                        
                        // B. 全量覆盖缓存本地的大厅数据（供各处随时读取）
                        localContext.LobbyPlayers = syncPacket.Players;
                        
                        // C. 【事件分发】：通知大厅 UI 页面重新生成玩家方块、名字和准备标签
                        OnLobbySyncReceived?.Invoke(syncPacket.Players);
                        break;

                    case PacketType.ServerGameStart:
                        ClientDebugger.LogHandler?.Invoke($"[Client] 收到开局指令，准备切入游戏！");
                        // 【事件分发】：通知 UI 层，执行 GetTree().ChangeSceneToFile() 切入联机对战主舞台场景
                        OnGameStartReceived?.Invoke();
                        break;

                    case PacketType.FrameData:
                        // 【帧同步灵魂纽带】：这里收到的是全网所有人的操作指令集合
                        var framePacket = new FramePacket();
                        framePacket.Deserialize(dataReader);
                        
                        // 【核心级联】：打破网络层限制，直接将这一物理逻辑帧推入状态机的抖动缓冲区（Jitter Buffer）中！
                        // 随后由 Godot 主线程定频（50ms）去消费和跑确定性数学运算
                        ServerCommandHandler.PushFrame(framePacket);
                        break;
                }
            };
        }

        // ==========================================
        // ===== 【泛型封装】通用的发送协议包方法 =====
        // ==========================================
        
        /// <summary>
        /// 通用发包接口：支持将任何实现了 INetSerializable 的协议包对象序列化并扔给服务器
        /// </summary>
        /// <typeparam name="T">约束：必须实现 LiteNetLib 的可序列化接口</typeparam>
        /// <param name="type">此协议包对应的标准类型包头标签</param>
        /// <param name="packet">具体的协议数据对象</param>
        public void SendPacket<T>(PacketType type, T packet) where T : INetSerializable
        {
            // 防御：没连上服务器前，不允许乱发包
            if (_serverPeer == null) return;
            
            // 1. 从内存池中拿或者实例化一个二进制写入器
            NetDataWriter writer = new NetDataWriter();
            
            // 2. 【核心步骤】：一定要先写入 1 个字节的包头！方便服务器的 NetworkReceiveEvent 能够用 GetByte() 对齐读取
            writer.Put((byte)type);
            
            // 3. 调用协议包对象本身的序列化实现，将对象的字段（如 Nickname, SlotId 等）按顺序打成二进制流
            packet.Serialize(writer);
            
            // 4. 【物理发送】：通过可靠有序管道（DeliveryMethod.ReliableOrdered）发给服务器
            // 确保协议包绝对不丢包、绝对不乱序到达
            _serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        /// <summary>
        /// 发起物理网络连接
        /// </summary>
        /// <param name="host">目标服务器 IP 地址（如 127.0.0.1）</param>
        /// <param name="port">目标服务器物理端口（如 5721）</param>
        /// <param name="connectionKey">联机握手校验密钥，必须与服务端 AppKey 一致才放行</param>
        public void Connect(string host, int port, string connectionKey)
        {
            // 启动底层网络套接字驱动
            _client.Start();
            // 向目标服务器异步建立物理连接，并拿回对端 Peer 句柄
            _serverPeer = _client.Connect(host, port, connectionKey);
        }

        /// <summary>
        /// 【网络引擎心脏】：网络事件驱动轮询
        /// 核心机制：由于移除了独立网络线程，必须挂在 Godot 的全局 _Process(delta) 中每帧高频调用！
        /// 只有调用了它，上面的所有事件（如 NetworkReceiveEvent）才会安全地在 Godot 的【主线程】中被触发回调。
        /// </summary>
        public void PollEvents() => _client.PollEvents();

        /// <summary>
        /// 断开网络连接并清理底层物理套接字
        /// </summary>
        public void Stop() => _client?.Stop();
    }
}