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
    
}