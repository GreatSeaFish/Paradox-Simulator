using System;
using LiteNetLib;
using LiteNetLib.Utils;
using Shared.Protocol;

namespace Server
{
    /// <summary>
    /// 帧同步多人在线游戏服务端核心控制类 (调度中枢)
    /// 职责：负责串联网络层、大厅管理器、帧同步引擎，处理底层物理网关事件分发。
    /// </summary>
    public class GameServer
    {
        // ==========================================
        // ===== 底层网络组件 =====
        // ==========================================
        
        /// <summary> LiteNetLib 网络管理器：负责底层的 UDP 套接字收发、物理连接维护 </summary>
        private NetManager _netManager = null!;
        
        /// <summary> LiteNetLib 事件监听器：基于事件驱动（如收到连接、收到网络包等物理回调） </summary>
        private EventBasedNetListener _listener = null!;

        /// <summary> 当前房间或游戏允许的最大玩家数量限制 </summary>
        public const int MaxPlayerCount = 8;

        // ===== 核心业务模块 =====
        private LobbyManager _lobbyManager = null!;
        private FrameSyncEngine _frameSyncEngine = null!;

        public void Start()
        {
            Console.WriteLine("=== 帧同步服务端启动 (大厅版) ===");

            // 初始化业务模块
            _frameSyncEngine = new FrameSyncEngine();
            
            // 1. 初始化 LiteNetLib 监听器与核心网络管理器
            _listener = new EventBasedNetListener();
            _netManager = new NetManager(_listener);
            
            // 注入依赖：大厅和引擎都需要使用网络组件来发包
            _lobbyManager = new LobbyManager(_netManager, _frameSyncEngine, MaxPlayerCount);
            _frameSyncEngine.Init(_netManager); 

            // 2. 指定服务端监听的物理 UDP 端口（例如 5721）
            _netManager.Start(5721); 
            Console.WriteLine($"[Server] 服务端已启动，最大支持 {MaxPlayerCount} 名玩家...");

            // 绑定所有网络网关回调
            BindNetworkEvents();

            // 3. 独立逻辑线程：启动高精度定时器（帧同步心脏循环）
            _frameSyncEngine.StartTickLoop();
        }

        private void BindNetworkEvents()
        {
            // --------------------------------------------------
            // 物理网关事件 1：处理客户端的物理握手连接请求
            // --------------------------------------------------
            _listener.ConnectionRequestEvent += request =>
            {
                // 人数防御：如果当前在线人数已经达到了房间最大上限，则直接拒绝新的连接
                if (_netManager.ConnectedPeersCount >= MaxPlayerCount)
                {
                    Console.WriteLine($"[Server] 拒绝连接：房间已满");
                    request.Reject();
                    return;
                }
                // 密钥防外挂验证：连接密码必须与客户端的 connectionKey ("FrameSyncDemo") 完全一致才放行
                request.AcceptIfKey("FrameSyncDemo"); 
            };

            // --------------------------------------------------
            // 物理网关事件 2：物理连接建立成功回调（握手成功）
            // --------------------------------------------------
            _listener.PeerConnectedEvent += peer =>
            {
                Console.WriteLine($"[Server] 玩家已连接: 物理PeerID={peer.Id}");
                
                // 实例化初始化协议包，下发给刚进来的客户端，告知其被分配的唯一玩家 ID
                var initPacket = new InitPacket
                {
                    AssignedPlayerId = peer.Id, // 直接使用 LiteNetLib 的底层 Peer.Id 作为玩家唯一标识，高效且天生互斥
                    TotalPlayersRequired = MaxPlayerCount
                };
                
                // 将数据对象打成二进制流并塞入可靠有序（ReliableOrdered）通道，保证客户端绝对能完整收到 ID
                NetDataWriter writer = new NetDataWriter();
                writer.Put((byte)PacketType.ServerInit); // 【核心边界】：写入 1 字节包头，告诉客户端准备用 InitPacket 解包
                initPacket.Serialize(writer);
                peer.Send(writer, DeliveryMethod.ReliableOrdered);
            };

            // --------------------------------------------------
            // 物理网关事件 3：物理连接断开回调（玩家主动退出、卡网掉线等）
            // --------------------------------------------------
            _listener.PeerDisconnectedEvent += (peer, disconnectInfo) =>
            {
                _lobbyManager.HandlePeerDisconnected(peer);
            };

            // --------------------------------------------------
            // 物理网关事件 4：【核心】网络消息接收分发中心
            // 所有从网络上漂过来的二进制数据流，均在这个物理 Lambda 回调中被拦截解包
            // --------------------------------------------------
            _listener.NetworkReceiveEvent += (fromPeer, dataReader, channel, deliveryMethod) =>
            {
                try
                {
                    // 【关键契约】：必须先精准读取第一个 Byte 作为业务包头标签
                    PacketType packetType = (PacketType)dataReader.GetByte();

                    // 根据包头进行业务解耦分流
                    switch (packetType)
                    {
                        case PacketType.ClientJoinLobby:
                            _lobbyManager.HandleClientJoinLobby(fromPeer, dataReader);
                            break;

                        case PacketType.ClientUpdateSlotColor:
                            _lobbyManager.HandleClientUpdateSlotColor(fromPeer, dataReader);
                            break;

                        case PacketType.ClientToggleReady:
                            _lobbyManager.HandleClientToggleReady(fromPeer, dataReader);
                            break;

                        case PacketType.FrameData:
                            _frameSyncEngine.HandleFrameData(fromPeer, dataReader);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Server] 解析数据包失败: {ex.Message}");
                }
            };
        }

        public void PollEvents()
        {
            _netManager?.PollEvents();
        }

        public void Stop()
        {
            _frameSyncEngine?.Stop();
            _netManager?.Stop();
        }
    }
}