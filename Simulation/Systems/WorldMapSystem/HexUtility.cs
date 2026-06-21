using System;
using System.Collections.Generic;

namespace ParadoxSimulator.Simulation.Systems.WorldMapSystem;
/// <summary>
/// 六边形网格纯静态工具类 (帧同步绝对确定性保证)
/// </summary>
public static class HexUtility
{
    // 顺时针（或逆时针，取决于坐标系映射）的六个相邻方向
    // 顺序一旦定义就不要修改，它保证了所有生成算法的确定性
    public static readonly HexCoord[] Directions = new HexCoord[]
    {
        new HexCoord(1, -1, 0),  // 0: 右上
        new HexCoord(1, 0, -1),  // 1: 右
        new HexCoord(0, 1, -1),  // 2: 右下
        new HexCoord(-1, 1, 0),  // 3: 左下
        new HexCoord(-1, 0, 1),  // 4: 左
        new HexCoord(0, -1, 1)   // 5: 左上
    };

    /// <summary>
    /// 1. 计算两个六边形之间的绝对距离 (步数)
    /// </summary>
    public static int GetDistance(HexCoord a, HexCoord b)
    {
        // 立方体坐标的曼哈顿距离除以2，或者直接取X,Y,Z绝对差值的最大值
        return Math.Max(Math.Abs(a.X - b.X), 
               Math.Max(Math.Abs(a.Y - b.Y), Math.Abs(a.Z - b.Z)));
    }

    /// <summary>
    /// 2. 获取指定方向上的单个邻居坐标
    /// </summary>
    /// <param name="directionIndex">方向索引 (0-5)</param>
    public static HexCoord GetNeighbor(HexCoord hex, int directionIndex)
    {
        // 取模保证索引安全，+6防止负数
        int safeIndex = ((directionIndex % 6) + 6) % 6;
        return hex + Directions[safeIndex];
    }

    /// <summary>
    /// 3. 获取周围一圈的所有 6 个邻居
    /// 保证返回的 List 顺序永远是确定的（固定从方向0到方向5）
    /// </summary>
    public static List<HexCoord> GetAllNeighbors(HexCoord center)
    {
        var neighbors = new List<HexCoord>(6);
        for (int i = 0; i < 6; i++)
        {
            neighbors.Add(center + Directions[i]);
        }
        return neighbors;
    }

    /// <summary>
    /// 4. 获取中心点周围“指定距离（N环）内”的【所有】格子集合
    /// 包含中心点，返回顺序严格由数学边界决定，确保帧同步一致性
    /// </summary>
    public static List<HexCoord> GetHexesInRange(HexCoord center, int range)
    {
        var results = new List<HexCoord>();
        
        // 严格的整数迭代，不会产生任何由于浮点或哈希造成的乱序
        for (int x = -range; x <= range; x++)
        {
            // 六边形的边界约束：Y的范围受到X的限制
            int yMin = Math.Max(-range, -x - range);
            int yMax = Math.Min(range, -x + range);
            
            for (int y = yMin; y <= yMax; y++)
            {
                int z = -x - y; // 满足 X + Y + Z = 0
                results.Add(new HexCoord(center.X + x, center.Y + y, center.Z + z));
            }
        }
        return results;
    }

    /// <summary>
    /// 5. (进阶拓展) 获取中心点周围“精准位于第 N 环”的格子集合 (空心环)
    /// 常用于技能范围边缘判定、生成光环特效等
    /// </summary>
    public static List<HexCoord> GetHexesInRing(HexCoord center, int radius)
    {
        var results = new List<HexCoord>();
        if (radius <= 0)
        {
            results.Add(center);
            return results;
        }

        // 找到第 N 环的起始点：向方向4移动 radius 步
        HexCoord current = center + new HexCoord(Directions[4].X * radius, Directions[4].Y * radius, Directions[4].Z * radius);

        // 沿着环绕圈遍历，每个方向走 radius 步
        for (int i = 0; i < 6; i++)
        {
            for (int j = 0; j < radius; j++)
            {
                results.Add(current);
                // 沿着下一个方向移动
                current = GetNeighbor(current, i);
            }
        }
        return results;
    }
}