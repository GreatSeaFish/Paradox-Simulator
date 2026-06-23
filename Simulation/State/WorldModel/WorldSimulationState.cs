using System;
using System.Collections.Generic;
using Shared.Math;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;

namespace ParadoxSimulator.Simulation.State.WorldModel;

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
    
    // 局内全网所有正在进行的殖民任务 (Key: 目标地块坐标, Value: 殖民任务详情)
    public Dictionary<HexCoord, ColonizationTask> ActiveColonizations { get; set; } = new Dictionary<HexCoord, ColonizationTask>();
    
    // 局内全网所有正在进行的造兵任务 (Key: 目标地块坐标, Value: 造兵任务详情)
    public Dictionary<HexCoord, UnitBuildTask> ActiveUnitBuilds { get; set; } = new Dictionary<HexCoord, UnitBuildTask>();

    // 局内全网所有已部署的军事单位 (Key: 目标地块坐标, Value: 单位详情)
    // 假设每个地块最多驻扎一支大部队
    public Dictionary<HexCoord, MilitaryUnit> DeployedUnits { get; set; } = new Dictionary<HexCoord, MilitaryUnit>();
    
    // ================== 时钟 ==================
    
    // 当前档位（0=暂停, 1~5=正常速度）
    public int _currentSpeedLevel = 0; 
    
    // 累计经历的逻辑帧数
    public int LocalTickCount { get; set; } = 0;
    
    // 游戏经过的天数
    public int GameDays { get; set; } = 0;
    
    // 【重构】模拟现实的日历字段，改用自定义确定的 GameDateTime (默认从 1年4月1日 开始)
    public GameDateTime CurrentDate { get; set; } = new GameDateTime(1, 4, 1);

    // ==========================================
    // =====          数据变更事件           =====
    // ==========================================
    
    // 抛出事件：供 UI 层（WorldRender/Hud）挂载监听，实现数据单向驱动 UI
    public event Action<int>? OnSpeedChanged;
    
    /// <summary> 实时资金变动事件（通知 UI 的 Amount） </summary>
    public event Action<int, int>? OnPlayerRealtimeFundsChanged;

    /// <summary> 月度预期/账单资金变动事件（通知 UI 的 Change） </summary>
    public event Action<int, int>? OnPlayerMonthlyExpectedChanged;

    /// <summary> 【重构】解耦后的时间与结算专属事件（参数调整为确定性 GameDateTime） </summary>
    public event Action<GameDateTime>? OnDateChanged;
    public event Action? OnDailySettlementRequired;
    public event Action? OnMonthlySettlementRequired;
    
    // 当地块的归属权发生实质性变更时触发
    public event Action<HexCoord, int>? OnTileOwnershipChanged;
    
    // 当造兵任务开始时触发 (用于UI显示造兵倒计时)
    public event Action<HexCoord, int>? OnUnitBuildStarted;
    
    // 当部队正式部署时触发 (用于实例化 MilitaryToken)
    public event Action<HexCoord, int, int>? OnUnitSpawned;
    
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
    /// 由 ServerCommandHandler 在处理到特定的逻辑帧指令时调用
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
    /// 【核心步进驱动】向前演进游戏内的一天，级联触发事件和结算信号（完全断开对系统 DateTime 的依赖）
    /// </summary>
    public void AdvanceDay()
    {
        GameDays++;
        
        // 记录演进前的月份，用于跨月断言
        int oldMonth = CurrentDate.Month;
        
        // 使用确定性方法向前推进一天
        CurrentDate = CurrentDate.AddDays(1);
        
        // 1. 优先抛出日期变更事件驱动 UI 展现
        OnDateChanged?.Invoke(CurrentDate);
        
        // 2. 触发每日结算业务逻辑（如每日消耗、任务倒计时扣减等）
        OnDailySettlementRequired?.Invoke();
        
        // 3. 检查是否跨月，触发跨月结算（如月度财政资源产出/扣除）
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
    /// 部署部队并通知渲染层
    /// </summary>
    public void SpawnUnit(HexCoord coord, int ownerId, int headcount)
    {
        // 写入状态机字典
        DeployedUnits[coord] = new MilitaryUnit { OwnerId = ownerId, Headcount = headcount };
        // 抛出事件通知外层 UI 生成预制体
        OnUnitSpawned?.Invoke(coord, ownerId, headcount);
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
            // 如果新主人和老主人不同，才修改并抛出事件
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
    
    /// <summary>
    /// 纯数据结构：记录单个地块的造兵任务状态
    /// </summary>
    public class UnitBuildTask
    {
        public int PlayerId { get; set; }
        // 造兵剩余天数，初始为 30
        public int RemainingDays { get; set; } 
    }

    /// <summary>
    /// 纯数据结构：记录部署在地块上的军事单位
    /// </summary>
    public class MilitaryUnit
    {
        public int OwnerId { get; set; }
        // 当前兵力，初始为 1000
        public int Headcount { get; set; } 
    }
}