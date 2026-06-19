using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using LiteNetLib;
using LiteNetLib.Utils;
using Shared.Protocol;

namespace Server
{
    class ServerMain
    {
        private static NetManager _netManager = null!;
        private static EventBasedNetListener _listener = null!;
        private static readonly ConcurrentQueue<PlayerCommand> _inputBuffer = new();
        private static readonly Dictionary<int, FramePacket> _frameHistory = new();
        
        // ===== 【新增】大厅状态管理 =====
        private static readonly Dictionary<int, LobbyPlayerInfo> _lobbyPlayers = new();
        private static readonly object _lobbyLock = new object();
        private const int MaxPlayerCount = 8; // 支持最大8人房间
        
        private static int _currentFrameId = 1;
        private static bool _isRunning = true;
        private static bool _isGameStarted = false;

        static void Main(string[] args)
        {
            Console.WriteLine("=== 帧同步服务端启动 (大厅版) ===");

            _listener = new EventBasedNetListener();
            _netManager = new NetManager(_listener);
            _netManager.Start(5721); 
            Console.WriteLine($"[Server] 服务端已启动，最大支持 {MaxPlayerCount} 名玩家...");

            // 1. 处理连接请求
            _listener.ConnectionRequestEvent += request =>
            {
                if (_netManager.ConnectedPeersCount >= MaxPlayerCount)
                {
                    Console.WriteLine($"[Server] 拒绝连接：房间已满");
                    request.Reject();
                    return;
                }
                request.AcceptIfKey("FrameSyncDemo"); 
            };

            // 2. 玩家连接成功：下发分配的 ID
            _listener.PeerConnectedEvent += peer =>
            {
                Console.WriteLine($"[Server] 玩家已连接: 物理PeerID={peer.Id}");
                
                var initPacket = new InitPacket
                {
                    AssignedPlayerId = peer.Id,
                    TotalPlayersRequired = MaxPlayerCount
                };
                
                NetDataWriter writer = new NetDataWriter();
                writer.Put((byte)PacketType.ServerInit); // 【关键】写入包头
                initPacket.Serialize(writer);
                peer.Send(writer, DeliveryMethod.ReliableOrdered);
            };

            // 3. 玩家断开连接：移出大厅并广播
            _listener.PeerDisconnectedEvent += (peer, disconnectInfo) =>
            {
                lock (_lobbyLock)
                {
                    if (_lobbyPlayers.Remove(peer.Id))
                    {
                        Console.WriteLine($"[Server] 玩家 {peer.Id} 已断开连接并移出大厅。");
                        BroadcastLobbySync();
                    }
                }
            };

            // 4. 核心：网络消息分发中心
            _listener.NetworkReceiveEvent += (fromPeer, dataReader, channel, deliveryMethod) =>
            {
                try
                {
                    // 【关键】先读取一个 Byte 的包头，判断是哪种类型的数据包
                    PacketType packetType = (PacketType)dataReader.GetByte();

                    switch (packetType)
                    {
                        case PacketType.ClientJoinLobby:
                            HandleClientJoinLobby(fromPeer, dataReader);
                            break;

                        case PacketType.ClientUpdateSlotColor:
                            HandleClientUpdateSlotColor(fromPeer, dataReader);
                            break;

                        case PacketType.ClientToggleReady:
                            HandleClientToggleReady(fromPeer, dataReader);
                            break;

                        case PacketType.FrameData:
                            // 只有游戏开始后，才接收帧指令
                            if (_isGameStarted)
                            {
                                var command = new PlayerCommand();
                                command.Deserialize(dataReader);
                                command.PlayerId = fromPeer.Id; 
                                _inputBuffer.Enqueue(command);
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Server] 解析数据包失败: {ex.Message}");
                }
            };

            // 启动逻辑帧循环线程
            Thread tickThread = new Thread(ServerTickLoop) { IsBackground = true };
            tickThread.Start();

            while (_isRunning)
            {
                _netManager.PollEvents();
                Thread.Sleep(15); 
            }

            _netManager.Stop();
        }

        #region 大厅逻辑处理

        private static void HandleClientJoinLobby(NetPeer peer, NetDataReader reader)
        {
            if (_isGameStarted) return; // 游戏开始了不让进大厅

            var joinPacket = new ClientJoinLobbyPacket();
            joinPacket.Deserialize(reader);

            lock (_lobbyLock)
            {
                // 分配一个空闲的插槽和颜色
                int slot = GetAvailableSlot();
                int color = GetAvailableColor();

                var newPlayer = new LobbyPlayerInfo
                {
                    PlayerId = peer.Id,
                    Nickname = joinPacket.Nickname,
                    SlotId = slot,
                    ColorId = color,
                    IsReady = false
                };

                _lobbyPlayers[peer.Id] = newPlayer;
                Console.WriteLine($"[Server] 玩家 {joinPacket.Nickname}(ID:{peer.Id}) 加入了大厅。分配位置:{slot}, 颜色:{color}");
                
                BroadcastLobbySync();
            }
        }

        private static void HandleClientUpdateSlotColor(NetPeer peer, NetDataReader reader)
        {
            if (_isGameStarted) return;

            var updatePacket = new ClientUpdateSlotColorPacket();
            updatePacket.Deserialize(reader);

            lock (_lobbyLock)
            {
                if (_lobbyPlayers.TryGetValue(peer.Id, out var player))
                {
                    // 检查冲突：如果别人已经选了这个位置或颜色，就忽略请求（由于之后会广播全量状态，客户端的错误UI会被自动纠正回旧状态）
                    bool slotConflict = _lobbyPlayers.Values.Any(p => p.PlayerId != peer.Id && p.SlotId == updatePacket.SlotId);
                    bool colorConflict = _lobbyPlayers.Values.Any(p => p.PlayerId != peer.Id && p.ColorId == updatePacket.ColorId);

                    if (!slotConflict && !colorConflict)
                    {
                        player.SlotId = updatePacket.SlotId;
                        player.ColorId = updatePacket.ColorId;
                        // 修改配置后自动取消准备状态，更加符合直觉
                        player.IsReady = false; 
                    }
                    
                    BroadcastLobbySync();
                }
            }
        }

        private static void HandleClientToggleReady(NetPeer peer, NetDataReader reader)
        {
            if (_isGameStarted) return;

            var readyPacket = new ClientToggleReadyPacket();
            readyPacket.Deserialize(reader);

            lock (_lobbyLock)
            {
                if (_lobbyPlayers.TryGetValue(peer.Id, out var player))
                {
                    player.IsReady = readyPacket.IsReady;
                    Console.WriteLine($"[Server] 玩家 {player.Nickname} 准备状态: {player.IsReady}");
                    BroadcastLobbySync();
                    CheckGameStart();
                }
            }
        }

        private static void BroadcastLobbySync()
        {
            var syncPacket = new ServerLobbySyncPacket { Players = _lobbyPlayers.Values.ToList() };
            NetDataWriter writer = new NetDataWriter();
            
            writer.Put((byte)PacketType.ServerLobbySync); // 包头
            syncPacket.Serialize(writer);
            
            _netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
        }

        private static void CheckGameStart()
        {
            // 开局条件：至少 2 人，且所有人都准备了
            if (_lobbyPlayers.Count >= 2 && _lobbyPlayers.Values.All(p => p.IsReady))
            {
                _isGameStarted = true;
                Console.WriteLine("\n[Server] ======================================");
                Console.WriteLine("[Server] 所有玩家已准备，正式开局！开启帧心跳...");
                Console.WriteLine("[Server] ======================================\n");

                NetDataWriter writer = new NetDataWriter();
                writer.Put((byte)PacketType.ServerGameStart); // 开局指令包头
                _netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
            }
        }

        // 辅助方法：获取空闲位置
        private static int GetAvailableSlot()
        {
            for (int i = 0; i < MaxPlayerCount; i++)
                if (!_lobbyPlayers.Values.Any(p => p.SlotId == i)) return i;
            return 0;
        }

        // 辅助方法：获取空闲颜色
        private static int GetAvailableColor()
        {
            for (int i = 0; i < MaxPlayerCount; i++)
                if (!_lobbyPlayers.Values.Any(p => p.ColorId == i)) return i;
            return 0;
        }

        #endregion

        #region 帧同步核心循环

        private static void ServerTickLoop()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            
            long nextTickTime = sw.ElapsedMilliseconds + 50;

            while (_isRunning)
            {
                long currentTime = sw.ElapsedMilliseconds;

                if (currentTime >= nextTickTime)
                {
                    if (_isGameStarted)
                    {
                        UpdateFrame();
                    }
                    nextTickTime += 50;
                }
                Thread.Sleep(1);
            }
        }

        private static void UpdateFrame()
        {
            var packet = new FramePacket { FrameId = _currentFrameId };

            while (_inputBuffer.TryDequeue(out var cmd))
            {
                packet.Commands.Add(cmd);
            }

            _frameHistory[_currentFrameId] = packet;

            if (_netManager.ConnectedPeersCount > 0)
            {
                NetDataWriter writer = new NetDataWriter();
                writer.Put((byte)PacketType.FrameData); // 【关键】发送逻辑帧前加上包头
                packet.Serialize(writer);
                
                _netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
                
                // 为了控制台不刷屏，可以注释掉下面这行
                // Console.WriteLine($"[Frame {_currentFrameId}] 已广播，包含 {packet.Commands.Count} 条指令");
            }

            _currentFrameId++;
        }

        #endregion
    }
}