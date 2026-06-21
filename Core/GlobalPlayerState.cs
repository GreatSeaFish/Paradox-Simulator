using System.Collections.Generic;
using FixedMath.NET;
using Shared.Math;

namespace ParadoxSimulator.Core
{
    /// <summary>
    /// 全局玩家状态中心（静态数据仓库）
    /// 职责：管理并缓存所有玩家的物理逻辑坐标。
    /// </summary>
    public static class GlobalPlayerState
    {
        // [新增] 预设 1-8 号位置的出生点坐标 (你可以根据 terrain_data.json 里的可用平地自行微调)
        public static readonly HexCoord[] SpawnPoints = new HexCoord[]
        {
            new HexCoord(0, 0, 0),      // Slot 0 (位置1)
            new HexCoord(3, -3, 0),     // Slot 1 (位置2)
            new HexCoord(-3, 3, 0),     // Slot 2 (位置3)
            new HexCoord(3, 0, -3),     // Slot 3 (位置4)
            new HexCoord(-3, 0, 3),     // Slot 4 (位置5)
            new HexCoord(0, 3, -3),     // Slot 5 (位置6)
            new HexCoord(0, -3, 3),     // Slot 6 (位置7)
            new HexCoord(6, -3, -3)     // Slot 7 (位置8)
        };
        /// <summary>
        /// 全房间所有玩家的【最新逻辑帧坐标】集合（Key: PlayerId, Value: 定点数坐标）
        /// </summary>
        public static readonly Dictionary<int, FixVector2> PlayerPositions = new();  

        /// <summary>
        /// 缓存的本地玩家最新输入 direction 向量（定点数）
        /// </summary>
        public static FixVector2 LocalInputDirection { get; set; } = FixVector2.Zero;  



        /// <summary>
        /// 直接获取当前确定的确定性逻辑位置，不再进行插值计算
        /// </summary>
        public static void GetLogicalPositions(Dictionary<int, FixVector2> outPos)
        {
            foreach (var kvp in PlayerPositions) 
            {
                outPos[kvp.Key] = kvp.Value;
            }
        }


        /// <summary>
        /// 设置本地输入方向缓存，供逻辑渲染解耦使用
        /// </summary>
        public static void SetLocalInput(FixVector2 direction)
        {
            LocalInputDirection = direction; 
        }
    }
}