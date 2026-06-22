using System;
using System.Collections.Generic;
using Shared.Math;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;

namespace ParadoxSimulator.Simulation.State;

/// <summary>
/// 核心帧同步状态仓库 (必须保证多端绝对一致)
/// </summary>
public class WorldSimulationState
{
    // ================== 实体 ==================
    // 全房间所有玩家的最新逻辑帧坐标
    public Dictionary<int, FixVector2> PlayerPositions { get; set; } = new Dictionary<int, FixVector2>();

    // 地块动态运行时的归属数据 (Key: 坐标, Value: 玩家ID，-1 表示中立)
    public Dictionary<HexCoord, int> TileOwners { get; set; } = new Dictionary<HexCoord, int>();
    
    // 局内全房间所有玩家的确定性资金数据 (Key: 玩家ID, Value: 金额)
    public Dictionary<int, int> PlayerFunds { get; set; } = new Dictionary<int, int>();
    
    // 局内全房间所有玩家的“本月资金预期变化值” (Key: 玩家ID, Value: 预计变化金额)
    public Dictionary<int, int> PlayerMonthlyFundsChange { get; set; } = new Dictionary<int, int>();
    
    // 【新增】局内全网所有正在进行的殖民任务 (Key: 目标地块坐标, Value: 殖民任务详情)
    public Dictionary<HexCoord, ColonizationTask> ActiveColonizations { get; set; } = new Dictionary<HexCoord, ColonizationTask>();
    
    // ================== 时钟 ==================
    
    
    // ==========================================
    // ===== 时间流速控制中心 =====
    // ==========================================
    
    // 当前档位（0=暂停, 1~5=正常速度），你可以根据需要设定初始默认几速
    public int _currentSpeedLevel = 0; 
    

    // 累计经历的逻辑帧数
    public int LocalTickCount { get; set; } = 0;
    
    // 游戏经过的天数
    public int GameDays { get; set; } = 0;
    
    // 模拟现实的日历字段 (默认从 1年4月1日 开始)
    public DateTime CurrentDate { get; set; } = new DateTime(1, 4, 1);

    // ==========================================
    // =====          数据变更事件           =====
    // ==========================================
    
    // 抛出事件：供 UI 层（WorldRender/Hud）挂载监听，实现数据单向驱动 UI
    public event Action<int>? OnSpeedChanged;
    
    /// <summary> 实时资金变动事件（通知 UI 的 Amount） </summary>
    public event Action<int, int>? OnPlayerRealtimeFundsChanged;

    /// <summary> 月度预期/账单资金变动事件（通知 UI 的 Change） </summary>
    public event Action<int, int>? OnPlayerMonthlyExpectedChanged;
// 【新增】解耦后的时间与结算专属事件
    public event Action<DateTime>? OnDateChanged;
    public event Action? OnDailySettlementRequired;
    public event Action? OnMonthlySettlementRequired;
    // 【新增】当地块的归属权发生实质性变更时触发
    public event Action<HexCoord, int>? OnTileOwnershipChanged;
    
    /// <summary>
    /// 初始化或重连时，主动向 UI 对齐当前所有的资金状态
    /// </summary>
    public void NotifyFundsChanged(int playerId)
    {
        if (PlayerFunds.ContainsKey(playerId))
        {
            int currentFunds = PlayerFunds[playerId];
            int expectedChange = PlayerMonthlyFundsChange.TryGetValue(playerId, out int exp) ? exp : 0;
            
            // 同时触发两个事件，让 UI 各自对齐
            OnPlayerRealtimeFundsChanged?.Invoke(playerId, currentFunds);
            OnPlayerMonthlyExpectedChanged?.Invoke(playerId, expectedChange);
        }
    }
    
    // ==========================================
    // =====          数据管理方法           =====
    // ==========================================
    
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
    /// 【核心步进驱动】向前演进游戏内的一天，级联触发事件和结算信号
    /// </summary>
    public void AdvanceDay()
    {
        GameDays++;
        int oldMonth = CurrentDate.Month;
        CurrentDate = CurrentDate.AddDays(1);
        
        // 1. 优先抛出日期变更事件驱动 UI
        OnDateChanged?.Invoke(CurrentDate);
        
        // 2. 触发每日结算业务
        OnDailySettlementRequired?.Invoke();
        
        // 3. 检查是否跨月，触发跨月结算
        if (CurrentDate.Month != oldMonth)
        {
            OnMonthlySettlementRequired?.Invoke();
        }
    }
    
    /// <summary>
    /// 【实时资金变动方法】：用于造兵、买道具、即时扣费或每日流失
    /// 仅影响并通知 UI 的 Amount 
    /// </summary>
    public void AddFundsRealtime(int playerId, int amount)
    {
        if (PlayerFunds.ContainsKey(playerId))
        {
            PlayerFunds[playerId] += amount;
            OnPlayerRealtimeFundsChanged?.Invoke(playerId, PlayerFunds[playerId]);
        }
    }
    
    /// <summary>
    /// 【修改月度预期账单方法】：实时占领地块、军队增减时调用
    /// 仅影响并通知 UI 的 Change 标签，不改变玩家当前总资金
    /// </summary>
    public void SetMonthlyFundsChange(int playerId, int expectedChange)
    {
        if (PlayerFunds.ContainsKey(playerId))
        {
            PlayerMonthlyFundsChange[playerId] = expectedChange;
            OnPlayerMonthlyExpectedChanged?.Invoke(playerId, expectedChange);
        }
    }
    
    /// <summary>
    /// 直接获取当前确定的确定性逻辑位置，不再进行插值计算
    /// </summary>
    public void GetLogicalPositions(Dictionary<int, FixVector2> outPos)
    {
        foreach (var kvp in PlayerPositions) 
        {
            outPos[kvp.Key] = kvp.Value;
        }
    }
    
    /// <summary>
    /// 获取指定地块的归属玩家ID
    /// </summary>
    public int GetTileOwner(HexCoord coord)
    {
        if (TileOwners.TryGetValue(coord, out int ownerId))
        {
            return ownerId;
        }
        return -1;
    }

    /// <summary>
    /// 设置/更改指定地块的归属权
    /// </summary>
    public void SetTileOwner(HexCoord coord, int playerId)
    {
        if (TileOwners.ContainsKey(coord))
        {
            // 【核心修改】如果新主人和老主人不同，才修改并抛出事件
            if (TileOwners[coord] != playerId)
            {
                TileOwners[coord] = playerId;
                OnTileOwnershipChanged?.Invoke(coord, playerId);
            }
        }
    }
    
    
    // ==========================================
    // =====          数据结构               =====
    // ==========================================
    
    /// <summary>
    /// 纯数据结构：记录单个地块的殖民任务状态 (需保证多端确定性)
    /// </summary>
    public class ColonizationTask
    {
        public int PlayerId { get; set; }
        
        // 殖民剩余天数，初始为 100
        public int RemainingDays { get; set; } 
    }
}