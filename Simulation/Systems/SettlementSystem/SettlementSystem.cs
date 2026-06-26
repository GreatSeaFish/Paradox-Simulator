using System;
using System.Collections.Generic;
using ParadoxSimulator.Simulation.State;
using ParadoxSimulator.Simulation.State.WorldModel;
namespace ParadoxSimulator.Simulation.Systems.SettlementSystem
{
    public class SettlementSystem
    {
        private readonly WorldSimulationState _state;
        private readonly List<ISettlementProcessor> _processors = new();

        public SettlementSystem(WorldSimulationState state)
        {
            _state = state;
            
            // 绑定时间驱动事件
            _state.OnDailySettlementRequired += ExecuteDailySettlement;
            _state.OnMonthlySettlementRequired += ExecuteMonthlySettlement;

            // 注册系统默认的结算处理器
            RegisterProcessor(new ColonizationProcessor());
            RegisterProcessor(new UnitBuildProcessor());
            RegisterProcessor(new MonthlyFundsProcessor());
            RegisterProcessor(new UnitMoveProcessor());
            // 【新增】：挂载战斗处理器，保证它每天都会随日历一起跑
            RegisterProcessor(new CombatProcessor());
            RegisterProcessor(new OccupationProcessor()); // 【新增】注册占领处理器
        }

        /// <summary>
        /// 提供公开方法，允许外部或未来通过依赖注入(DI)动态添加新的结算业务
        /// </summary>
        public void RegisterProcessor(ISettlementProcessor processor)
        {
            if (processor == null) return;
            _processors.Add(processor);
        }

        /// <summary>
        /// 由 TimeSystem 驱动的每日业务结算
        /// </summary>
        public void ExecuteDailySettlement()
        {
            // 过滤并执行所有每日处理器
            foreach (var processor in _processors)
            {
                if (processor.Frequency == SettlementFrequency.Daily)
                {
                    processor.Execute(_state);
                }
            }
        }

        /// <summary>
        /// 由 TimeSystem 驱动的每月业务结算
        /// </summary>
        public void ExecuteMonthlySettlement()
        {
            // 过滤并执行所有每月处理器
            foreach (var processor in _processors)
            {
                if (processor.Frequency == SettlementFrequency.Monthly)
                {
                    processor.Execute(_state);
                }
            }
        }
    }
}