using System;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;

namespace ParadoxSimulator.Simulation.State.WorldModel;


public partial class WorldSimulationState
{
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
    // --- 在 StateChangeEvents.cs 中修改事件签名 ---
// 改为直接传递整个 MilitaryUnit 对象
    public event Action<MilitaryUnit>? OnUnitSpawned; 
// 改为传递 UnitId 和起止坐标
    public event Action<int, HexCoord, HexCoord>? OnUnitStepped; 
// 改为传递 UnitId
    public event Action<int>? OnUnitRemoved;
    // ==========================================
    // =====       【新增】战斗相关事件       =====
    // ==========================================
    
    // 当两支部队相撞，战斗爆发时触发 (可用于在 UI 上播放“拔剑”动画或音效)
    public event Action<CombatSession>? OnCombatStarted;
    
    // 每日战斗结算后触发，通知 UI 更新交战双方的兵力、士气进度条
    public event Action<CombatSession>? OnCombatUpdated;
    
    // 战斗结束时触发，传递 (CombatId, 胜利者的UnitId)。如果平局/同归于尽，胜利者传 -1
    public event Action<int, int>? OnCombatEnded;
    
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
    // 新增触发方法
    public void NotifyUnitStepped(int unitId, HexCoord from, HexCoord to)
    {
        OnUnitStepped?.Invoke(unitId, from, to);
    }
    // ==========================================
    // =====       战斗事件触发辅助方法       =====
    // ==========================================
    
    public void NotifyCombatStarted(CombatSession combat)
    {
        OnCombatStarted?.Invoke(combat);
    }

    public void NotifyCombatUpdated(CombatSession combat)
    {
        OnCombatUpdated?.Invoke(combat);
    }

    public void NotifyCombatEnded(int combatId, int winnerUnitId)
    {
        OnCombatEnded?.Invoke(combatId, winnerUnitId);
    }
}