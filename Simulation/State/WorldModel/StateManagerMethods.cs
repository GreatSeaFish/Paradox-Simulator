using System;
using System.Collections.Generic;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;
using Shared.Math;

namespace ParadoxSimulator.Simulation.State.WorldModel;

public partial class WorldSimulationState
{
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
    
}