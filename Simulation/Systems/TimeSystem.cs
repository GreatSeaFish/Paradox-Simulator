using System;
using ParadoxSimulator.Simulation;
using ParadoxSimulator.Simulation.State;

namespace ParadoxSimulator.Simulation.Systems
{
    /// <summary>
    /// 游戏内虚拟时间管理器（确定性时钟）
    /// 职责：管理时钟步进，并实例化与驱动 SettlementSystem
    /// </summary>
    public class TimeSystem(WorldSimulationState worldSimulationState)
    {

        // ==========================================
        // ===== 时间流速控制中心 =====
        // ==========================================

        
        // 当前档位（0=暂停, 1~5=正常速度），你可以根据需要设定初始默认几速
        private int _currentSpeedLevel = 0; 
        
        // 速度映射表：索引对应 UI 的档位，值对应“经过游戏内的一天需要多少个逻辑帧”
        // 索引 0 (暂停) = -1 (特殊标记)
        // 索引 1 (一速) = 16 帧 (0.8秒/天)
        // 索引 2 (二速) = 8 帧  (0.4秒/天)
        // 索引 3 (三速) = 4 帧  (0.2秒/天) -> 对齐了你原本的 TicksPerDay = 4
        // 索引 4 (四速) = 2 帧  (0.1秒/天)
        // 索引 5 (五速) = 1 帧  (0.05秒/天)
        private readonly int[] _speedMap = { -1, 16, 8, 4, 2, 1 };

        // 抛出事件：供 UI 层（WorldRender/Hud）挂载监听，实现数据单向驱动 UI
        public event Action<int>? OnSpeedChanged;


        /// <summary>
        /// 【新增】由 ServerCommandHandler 在处理到特定的逻辑帧指令时调用
        /// </summary>
        public void SetTimeSpeed(int level)
        {
            // 防御性编程：将传入的档位钳制在 0-5 范围内，防止恶意发包越界
            _currentSpeedLevel = Math.Clamp(level, 0, 5);
            ClientDebugger.LogHandler?.Invoke($"[TimeSystem] 时间流速确切切换至: {_currentSpeedLevel} 档");
            
            // 触发事件，通知表现层刷新 UI 的 Tab 高亮
            OnSpeedChanged?.Invoke(_currentSpeedLevel);
        }

        /// <summary>
        /// 由 ServerCommandHandler 驱动
        /// </summary>
        public void Tick()
        {
            if (_speedMap[_currentSpeedLevel] == -1) return;

            worldSimulationState.LocalTickCount++;
            if (worldSimulationState.LocalTickCount >= _speedMap[_currentSpeedLevel])
            {
                worldSimulationState.LocalTickCount = 0;
                
                // 仅推动数据层，不再亲自调度业务层
                worldSimulationState.AdvanceDay();
            }
        }
    }
}