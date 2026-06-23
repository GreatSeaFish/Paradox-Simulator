using ParadoxSimulator.Simulation.State;


// 策略模式
namespace ParadoxSimulator.Simulation.Systems.SettlementSystem
{
    public enum SettlementFrequency
    {
        Daily,
        Monthly
    }

    public interface ISettlementProcessor
    {
        /// <summary>
        /// 结算频率：每日或每月
        /// </summary>
        SettlementFrequency Frequency { get; }

        /// <summary>
        /// 执行具体的结算逻辑
        /// </summary>
        void Execute(WorldSimulationState state);
    }
}