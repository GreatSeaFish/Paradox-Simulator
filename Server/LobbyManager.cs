using System;
using System.Collections.Generic;
using System.Linq;
using LiteNetLib;
using LiteNetLib.Utils;
using Shared.Protocol;

namespace Server
{
    /// <summary>
    /// 职责：负责管理前期的游戏大厅（玩家加入、插槽颜色选择、准备就绪检查）。
    /// </summary>
    public class LobbyManager
    {
        // ==========================================
        // ===== 游戏大厅状态管理 =====
        // ==========================================
        
        /// <summary> 纯内存大厅玩家名单仓库（Key: 物理 PeerID, Value: 玩家大厅配置信息） </summary>
        private readonly Dictionary<int, LobbyPlayerInfo> _lobbyPlayers = new();
        
        /// <summary> 
        /// 大厅状态锁。
        /// 关键安全防御：由于 LiteNetLib 异步网络线程可能会同时触发“玩家断网”和“玩家发包更改颜色”，
        /// 必须使用此锁确保对 _lobbyPlayers 的增删改操作是线程安全的。
        /// </summary>
        private readonly object _lobbyLock = new object();

        private readonly NetManager _netManager;
        private readonly FrameSyncEngine _frameSyncEngine;
        private readonly int _maxPlayerCount;

        public LobbyManager(NetManager netManager, FrameSyncEngine frameSyncEngine, int maxPlayerCount)
        {
            _netManager = netManager;
            _frameSyncEngine = frameSyncEngine;
            _maxPlayerCount = maxPlayerCount;
        }

        public void HandlePeerDisconnected(NetPeer peer)
        {
            lock (_lobbyLock) // 涉及大厅字典修改，加锁防御
            {
                // 如果在字典里成功移除了这个玩家，说明他刚才确实在房间大厅里
                if (_lobbyPlayers.Remove(peer.Id))
                {
                    Console.WriteLine($"[Server] 玩家 {peer.Id} 已断开连接并移出大厅。");
                    // 某个玩家走了，必须立刻对剩下全员广播一份最新的名单，刷新客户端 UI
                    BroadcastLobbySync();
                }
            }
        }

        /// <summary>
        /// 处理客户端请求加入大厅的业务
        /// </summary>
        public void HandleClientJoinLobby(NetPeer peer, NetDataReader reader)
        {
            if (_frameSyncEngine.IsGameStarted) return; // 游戏已经开局了，直接丢弃该请求，不允许中途塞人插队

            var joinPacket = new ClientJoinLobbyPacket();
            joinPacket.Deserialize(reader); // 拆解出玩家本地输入的 Nickname

            lock (_lobbyLock) // 进入临界区，锁定大厅状态
            {
                // 自动分配算法：找到当前大厅中还没有被人占据的最小数字位置和闲置颜色
                int slot = GetAvailableSlot();
                int color = GetAvailableColor();

                // 实例化新的大厅常驻结构
                var newPlayer = new LobbyPlayerInfo
                {
                    PlayerId = peer.Id,
                    Nickname = joinPacket.Nickname,
                    SlotId = slot,
                    ColorId = color,
                    IsReady = false // 新进来的人默认一律是未准备状态
                };

                _lobbyPlayers[peer.Id] = newPlayer; // 将其编入队伍名单
                Console.WriteLine($"[Server] 玩家 {joinPacket.Nickname}(ID:{peer.Id}) 加入了大厅。分配位置:{slot}, 颜色:{color}");
                
                // 变动发生，全员大厅数据强制全量同步一次
                BroadcastLobbySync();
            }
        }

        /// <summary>
        /// 处理客户端在房间里手动切换位置 (SlotId) 或更换颜色 (ColorId) 的请求
        /// </summary>
        public void HandleClientUpdateSlotColor(NetPeer peer, NetDataReader reader)
        {
            if (_frameSyncEngine.IsGameStarted) return; // 游戏开始了，不允许随意切座位和皮肤

            var updatePacket = new ClientUpdateSlotColorPacket();
            updatePacket.Deserialize(reader); // 解出客户端期望修改的新插槽和新颜色

            lock (_lobbyLock)
            {
                if (_lobbyPlayers.TryGetValue(peer.Id, out var player))
                {
                    // 【互斥核心：防冲突检查】
                    // 检查除自己之外，房间里是否已经有人捷足先登占领了这个物理位置或者选了这个颜色
                    bool slotConflict = _lobbyPlayers.Values.Any(p => p.PlayerId != peer.Id && p.SlotId == updatePacket.SlotId);
                    bool colorConflict = _lobbyPlayers.Values.Any(p => p.PlayerId != peer.Id && p.ColorId == updatePacket.ColorId);

                    // 只有完全不冲突的情况下，才批准本次修改
                    if (!slotConflict && !colorConflict)
                    {
                        player.SlotId = updatePacket.SlotId;
                        player.ColorId = updatePacket.ColorId;
                        // 策略亮点：改动了房间配置或座位，出于审慎性，自动帮玩家取消准备，防止手滑
                        player.IsReady = false; 
                    }
                    
                    // 广播最新的大厅快照。
                    // 细节：如果刚才发生了冲突，客户端上传被无视了，由于这次重新广播的是旧大厅快照，
                    // 客户端的本地错误 UI 状态会被直接拉回、纠正为正确的数据。
                    BroadcastLobbySync();
                }
            }
        }

        /// <summary>
        /// 处理客户端点击“准备/取消准备”按钮
        /// </summary>
        public void HandleClientToggleReady(NetPeer peer, NetDataReader reader)
        {
            if (_frameSyncEngine.IsGameStarted) return;

            var readyPacket = new ClientToggleReadyPacket();
            readyPacket.Deserialize(reader); // 拿到最新的 bool 值

            lock (_lobbyLock)
            {
                if (_lobbyPlayers.TryGetValue(peer.Id, out var player))
                {
                    player.IsReady = readyPacket.IsReady; // 更新状态中心
                    Console.WriteLine($"[Server] 玩家 {player.Nickname} 准备状态: {player.IsReady}");
                    
                    BroadcastLobbySync(); // 广播全网，让所有人看到他的绿标亮起
                    CheckGameStart();     // 每次有人切换准备，顺便踩一脚“开局油门”，看是不是全员都好准备了
                }
            }
        }

        /// <summary>
        /// 核心基础方法：向当前全网大厅里的所有连接发送全量大厅名单
        /// </summary>
        private void BroadcastLobbySync()
        {
            // 组装广播协议包
            var syncPacket = new ServerLobbySyncPacket { Players = _lobbyPlayers.Values.ToList() };
            NetDataWriter writer = new NetDataWriter();
            
            writer.Put((byte)PacketType.ServerLobbySync); // 【关键】塞入包头标签 2
            syncPacket.Serialize(writer);
            
            // LiteNetLib 提供的组播广播接口，一发千钧，扔给全网所有人，使用高可靠管道
            _netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
        }

        /// <summary>
        /// 检查是否满足正式开局的所有条件
        /// </summary>
        private void CheckGameStart()
        {
            // 开局硬性约束：
            // 1. 房间必须至少有 2 个人参与（防止单人直接空转）
            // 2. 房间里的所有人其大厅结构中的 IsReady 字段必须全部为 true
            if (_lobbyPlayers.Count >= 2 && _lobbyPlayers.Values.All(p => p.IsReady))
            {
                _frameSyncEngine.IsGameStarted = true; // 状态门闩落下，不可逆切入对战阶段
                Console.WriteLine("\n[Server] ======================================");
                Console.WriteLine("[Server] 所有玩家已准备，正式开局！开启帧心跳...");
                Console.WriteLine("[Server] ======================================\n");

                NetDataWriter writer = new NetDataWriter();
                writer.Put((byte)PacketType.ServerGameStart); // 开局核心包头标签 5（不带任何字段，本身就是一个纯动作信号）
                _netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
            }
        }

        /// <summary>
        /// 大厅辅助计算：遍历 0-7 号座位，找出第一个没有人的坑位
        /// </summary>
        private int GetAvailableSlot()
        {
            for (int i = 0; i < _maxPlayerCount; i++)
                if (!_lobbyPlayers.Values.Any(p => p.SlotId == i)) return i;
            return 0;
        }

        /// <summary>
        /// 大厅辅助计算：遍历 0-7 号颜色索引，找出第一个没有被穿上的皮肤色
        /// </summary>
        private int GetAvailableColor()
        {
            for (int i = 0; i < _maxPlayerCount; i++)
                if (!_lobbyPlayers.Values.Any(p => p.ColorId == i)) return i;
            return 0;
        }
    }
}