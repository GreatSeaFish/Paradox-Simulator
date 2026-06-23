using System;
using System.Collections.Generic;
using Shared.Math;
using ParadoxSimulator.Simulation.Systems.WorldMapSystem;

namespace ParadoxSimulator.Simulation.State.WorldModel;

/// <summary>
/// 核心帧同步状态仓库 (必须保证多端绝对一致)
/// </summary>
public partial class WorldSimulationState
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
 
}