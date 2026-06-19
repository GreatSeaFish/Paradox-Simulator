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
        /// <summary>
        /// 全网所有玩家的【最新逻辑帧坐标】集合（Key: PlayerId, Value: 定点数坐标）
        /// </summary>
        public static readonly Dictionary<int, FixVector2> PlayerPositions = new();  

        /// <summary>
        /// 缓存的本地玩家最新输入 direction 向量（定点数）
        /// </summary>
        public static FixVector2 LocalInputDirection { get; set; } = FixVector2.Zero;  

        /// <summary>
        /// 开局游戏状态初始化：清空旧数据，并根据大厅玩家名单动态排开初始位置
        /// </summary>
        public static void InitializeGame()
        {
            PlayerPositions.Clear();  

            foreach (var player in LocalClientInfo.LobbyPlayers)  
            {
                // 根据 SlotId (房间位置 0~7) 错开初始坐标
                FixVector2 startPos = new FixVector2((Fix64)(player.SlotId * 5), Fix64.Zero);  
                PlayerPositions[player.PlayerId] = startPos;  
            }
        }

        /// <summary>
        /// 【已简化】直接获取当前确定的确定性逻辑位置，不再进行插值计算
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