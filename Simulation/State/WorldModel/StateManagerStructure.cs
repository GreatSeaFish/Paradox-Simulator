namespace ParadoxSimulator.Simulation.State.WorldModel;

public partial class WorldSimulationState
{
       
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