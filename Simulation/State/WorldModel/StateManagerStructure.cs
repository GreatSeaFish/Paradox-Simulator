using System.Collections.Generic;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;

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
    // 1. 修改部队实体：加上 UnitId 和当前位置
    public class MilitaryUnit
    {
        public int UnitId { get; set; }
        public int OwnerId { get; set; }
        public int Headcount { get; set; }
        public HexCoord CurrentLocation { get; set; } 
        
        // 【新增】：千分制士气值，初始满士气为 1000
        public int Morale { get; set; } = 1000; 
    }
    
    // 2. 修改行军任务：直接绑定 UnitId，不再散装数据
    public class UnitMoveTask
    {
        public int TaskId { get; set; }
        public int UnitId { get; set; } // 替代原有的 PlayerId / Headcount / CurrentLocation
        public int PlayerId { get; set; }
        public List<HexCoord> Waypoints { get; set; } = new List<HexCoord>();
        public int TotalDaysForNextTile { get; set; }
        public int RemainingDaysForNextTile { get; set; }
    }
    

    
    // 【新增】：纯数据结构：记录一场正在进行的战斗
    public class CombatSession
    {
        public int CombatId { get; set; }
        public int AttackerUnitId { get; set; }
        public int DefenderUnitId { get; set; }
        public HexCoord Location { get; set; }
        
        // 记录战斗爆发的游戏天数，可以用来计算战损或者超时强行撤退
        public int StartDay { get; set; } 
    }
}