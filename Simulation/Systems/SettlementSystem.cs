using System;

namespace ParadoxSimulator.Simulation.Systems
{
    /// <summary>
    /// 结算管理器
    /// </summary>
    public class SettlementSystem
    {
        /// <summary>
        /// 由 TimeSystem 驱动的每日业务结算
        /// </summary>
        public void ExecuteDailySettlement(DateTime date)
        {
            ClientDebugger.LogHandler?.Invoke($"[SettlementSystem] 触发【日结算】业务逻辑。当前游戏日期: {date:yyyy-MM-dd}");
            // TODO: 在这里处理每日流失、资源产出等与回合/阵营相关的业务
        }

        /// <summary>
        /// 由 TimeSystem 驱动的每月业务结算
        /// </summary>
        public void ExecuteMonthlySettlement(DateTime date)
        {
            ClientDebugger.LogHandler?.Invoke($"[SettlementSystem] 触发【月结算】业务逻辑！！！当前游戏月份: {date:yyyy-MM}");
            // TODO: 在这里处理每月税收、军队维护费扣除、大事件刷新等深层业务
        }
    }
}