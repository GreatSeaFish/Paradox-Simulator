using System;
using System.Collections.Generic;
using FixedMath.NET;
using ParadoxSimulator.Core.CommandSystem;
using ParadoxSimulator.Core.GameData;
using Shared.Protocol;

namespace ParadoxSimulator.Core
{
    /// <summary>
    /// 服务端指令处理中心（客户端逻辑驱动核心）
    /// 职责：管理客户端逻辑帧的推进、实现抖动缓冲区（Jitter Buffer）、弹性追帧决策与确定性状态计算。
    /// </summary>
    public class ServerCommandHandler(TimeManager timeManager, LocalContext localContext, WorldSimulationState worldSimulationState)
    {
        // ===== 帧缓冲区 (Jitter Buffer) =====
        // 存放从网络层（网络线程/轮询）推过来的原生服务器逻辑帧，Key 是 FrameId，Value 是该帧包含的所有指令
        private static readonly Dictionary<int, FramePacket> _frameBuffer = new();
        
        // 缓冲区锁：多线程安全防御（网络层写缓冲区，Godot主线程读缓冲区，必须加锁互斥）
        private static readonly object _bufferLock = new object();
        
        // 客户端当前【期待执行】的逻辑帧号。从1帧开始，每消耗完一帧就+1
        private int _localRenderFrameId = 1;
        
        // 冷启动就绪标记：刚进游戏时，必须积攒足够的帧才能开始跑，避免一进去就因为网络波动而一卡一卡
        private bool _isBufferReady = false;
        

        // ===== 时间累加器与固定间隔定义 =====
        // 记录距离上一次执行逻辑步进过去了多少秒（由 Godot 每帧的 delta 累加）
        private double _logicTimer = 0.0;
        
        // 严格固定 50ms (20Hz) 的逻辑步进心跳，意味着逻辑状态机的齿轮每 50ms 必须转动一步
        private const double LogicInterval = 0.050; 

        // ==================== 弹性追帧阈值定义 ====================
        // 【温和追帧阈值】：如果缓冲区积压了超过 3 帧（网络开始抖动），说明客户端落后了，需要加速推进
        private const int CatchUpThreshold = 3;
        
        // 【单次最大补帧数】：在温和追帧状态下，一个 50ms 周期内最多允许连续向前跑 2 步逻辑，防止用力过猛导致画面瞬间拉扯
        private const int MaxCatchUpStepsPerTick = 2;
        
        // 【断连/激进追帧阈值】：如果积压超过 10 帧，说明刚刚经历了切后台、严重卡顿或断线重连，必须全速暴走追赶
        private const int DisconnectCatchUpThreshold = 10;


        
        /// <summary>
        /// 【外部写入接口】当网络层收到来自服务端的 FrameData 包时，会高频调用此方法将帧压入缓冲区
        /// </summary>
        public static void PushFrame(FramePacket packet)
        {
            lock (_bufferLock)
            {
                // 将网络帧塞入字典缓存，等待主线程定频消耗
                _frameBuffer[packet.FrameId] = packet;
            }
        }

        /// <summary>
        /// ===== 【驱动引擎】定频心跳更新接口 =====
        /// 由主线程的while每帧高频调用
        /// </summary>
        /// <param name="delta">从 Godot 传进来的单帧渲染增量时间（秒）</param>
        public void Update(double delta)
        {
            // 还没连接成功或者还没拿到合法玩家ID之前，直接罢工，什么都不算
            if (localContext.MyPlayerId == -1) return;

            // 1. 累加渲染帧的 delta 时间（把渲染时间转化为逻辑时间储备）
            _logicTimer += delta;

            // 2. 只有当逻辑时间储备累加满 50ms 时，才触发日常的“一步”逻辑齿轮转动
            if (_logicTimer >= LogicInterval)
            {
                // 减去消耗掉的固定间隔，保留高精度时间余数防止时间缩水导致丢帧
                _logicTimer -= LogicInterval;

                int framesInCount = 0; // 当前缓冲区里积压的总帧数
                lock (_bufferLock)
                {
                    // 【冷启动检查】：首次开局的平滑攒帧机制
                    if (!_isBufferReady)
                    {
                        // 必须同时攒齐第 1 帧和第 2 帧，才允许客户端正式启动确定性运算
                        if (_frameBuffer.ContainsKey(1) && _frameBuffer.ContainsKey(2))
                        {
                            _isBufferReady = true;
                            ClientDebugger.LogHandler?.Invoke("[Client] 缓冲区已攒够，开始平滑驱动逻辑！");
                        }
                        else
                        {
                            // 【设计亮点】：由于帧还没攒够，把刚刚扣掉的 50ms 吐回去！
                            // 否则在等待开局攒帧的期间，计时器空转，一旦攒够帧就会因为时间严重超标而瞬间暴走追帧
                            _logicTimer += LogicInterval; 
                            return; // 结束当前更新，等下一个 Godot 帧再来看
                        }
                    }
                    framesInCount = _frameBuffer.Count; // 拿到当前缓冲区里积压的总网络帧数
                }

                // 默认的步进格数：正常网络下，每 50ms 对应消耗 1 步网络帧
                int stepsToRun = 1;

                // 【断连等待检查】：如果客户端接下来想跑的这一帧（_localRenderFrameId）服务器还没发过来，说明卡网了
                lock (_bufferLock)
                {
                    if (!_frameBuffer.ContainsKey(_localRenderFrameId))
                    {
                        // 没拿到帧就地挂起（此时 50ms 的时间已经扣了，画面会表现为卡住，原地死等服务器的包）
                        return;
                    }
                }

                // 3. 【核心算法：追帧决策】
                // 根据缓冲区积压的严重程度，决定当前 50ms 的心跳周期里要连续驱动多少步状态机
                if (framesInCount > DisconnectCatchUpThreshold)
                {
                    // 【激进追帧】：切后台或者断线重连恢复后，积压了几百帧，直接把积压的所有帧一口气跑完！
                    stepsToRun = framesInCount;
                    ClientDebugger.LogHandler?.Invoke($"[Client] 检测到严重积压({framesInCount}帧)，触发断线重连快速追帧！");
                }
                else if (framesInCount > CatchUpThreshold)
                {
                    // 【温和追帧】：轻微网络抖动，积压了4帧左右。我们在当前 50ms 周期里多跑 1 步（也就是跑 2 步）
                    // 既能悄悄把落后的进度追回来，又不会因为步伐太大导致渲染层插值拉扯畸变
                    stepsToRun = System.Math.Min(framesInCount, MaxCatchUpStepsPerTick);
                }

                // 4. 执行计算：根据追帧决策，把状态机向前推进 stepsToRun 步
                for (int i = 0; i < stepsToRun; i++)
                {
                    bool success = DriveSingleFrameInternal();
                    if (!success) break; // 如果某一步执行失败（卡网了），立刻中断，防止跨帧执行报错
                }
            }
        }
        
        
        /// <summary>
        /// 内部核心方法：严格处理并消耗一个单一逻辑帧的确定性业务计算
        /// </summary>
        /// <summary>
        /// 内部核心方法：严格处理并消耗一个单一逻辑帧的确定性业务计算
        /// </summary>
        private bool DriveSingleFrameInternal()
        {
            FramePacket? currentFramePacket = null;

            // 1. 从帧缓冲区中取出当前进度对应的网络包，成功拿到后立刻将其移出缓冲区（消费网络帧）
            lock (_bufferLock)
            {
                if (_frameBuffer.TryGetValue(_localRenderFrameId, out currentFramePacket))
                {
                    _frameBuffer.Remove(_localRenderFrameId);
                }
            }

            // 安全防御：万一真的没拿到（虽然前面检查过了，但这里做双重保险），进入卡顿等待
            if (currentFramePacket == null)
            {
                ClientDebugger.LogHandler?.Invoke($"[Warning] 网络卡顿！客户端缺少 Frame {_localRenderFrameId}，进入等待...");
                return false;
            }

            // 3. 【确定性业务逻辑计算】：开始解包这一帧里全网所有玩家（包含自己）在这个时间点的操作指令
            foreach (var cmdDto in currentFramePacket.Commands)
            {
                // // 【核心修改】：通过指令类型进行分流处理
                // switch (cmd.InputType)
                // {
                //     case 1: // 移动指令
                //         // 检查此玩家在当前局内是否存在位置数据
                //         if (worldSimulationState.PlayerPositions.ContainsKey(cmd.PlayerId))
                //         {
                //             // 严格使用定点数（Fix64）进行纯位移数学运算：新位置 = 旧位置 + 移动方向 * 速度因子
                //             // 绝对不涉及 Godot 的 Node 节点坐标，纯内存数学计算，多端完全一致
                //             worldSimulationState.PlayerPositions[cmd.PlayerId] += cmd.MoveDirection * Fix64.One;
                //         }
                //         break;
                //         
                //     case 2: // 系统指令：时间控制
                //         // 直接从指令的 ActionValue 中拿到档位，驱动时钟
                //         // 如果多个人在同一帧都按了调速，那么以最后一个指令为准，所有人表现依然绝对一致
                //         timeManager.SetTimeSpeed(cmd.ActionValue);
                //         break;
                // }
                
                // 1. 通过工厂将网络数据转化为逻辑指令
                IGameCommand? command = CommandFactory.Create(cmdDto);
    
                // 2. 无脑执行（多态派发）
                command?.Execute(worldSimulationState, timeManager, cmdDto.PlayerId);
                
            }

            // 4. 逻辑推进完毕，状态机时钟步进
            _localRenderFrameId++; // 当前期待帧号向前进1，指向下一帧
            
            timeManager.Tick();
            
            return true; // 成功消化完一帧
        }


    }
}