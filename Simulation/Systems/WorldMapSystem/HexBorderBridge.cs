using System;
using System.Collections.Generic;

namespace ParadoxSimulator.Simulation.Systems.WorldMapSystem;

/// <summary>
/// 六边形双网格桥接工具
/// 适用于：主地块尺寸为主，边界地块尺寸为一半的完美嵌套双网格系统
/// </summary>
public static class HexBorderBridge
{
    /// <summary>
    /// 1. 根据两个相邻的主地块坐标，获取它们之间的边界（河流/城墙）坐标
    /// </summary>
    public static HexCoord GetBorderCoord(HexCoord tileA, HexCoord tileB)
    {
        // 边界的 Cube 坐标恰好是两个相邻地块 Cube 坐标的向量之和
        return new HexCoord(tileA.X + tileB.X, tileA.Y + tileB.Y, tileA.Z + tileB.Z);
    }

    /// <summary>
    /// 2. 根据一个边界坐标，反推共享该边界的两个主地块坐标
    /// </summary>
    public static (HexCoord TileA, HexCoord TileB) GetTilesFromBorder(HexCoord border)
    {
        // 因为 Border = 2 * TileA + Dir
        // 遍历 6 个方向，寻找让 (Border - Dir) 的 XYZ 都能被 2 整除的方向
        foreach (var dir in HexUtility.Directions)
        {
            int diffX = border.X - dir.X;
            int diffY = border.Y - dir.Y;
            int diffZ = border.Z - dir.Z;

            // C# 中偶数对 2 取余必定为 0（兼容负偶数）
            if (diffX % 2 == 0 && diffY % 2 == 0 && diffZ % 2 == 0)
            {
                HexCoord tileA = new HexCoord(diffX / 2, diffY / 2, diffZ / 2);
                HexCoord tileB = new HexCoord((border.X + dir.X) / 2, (border.Y + dir.Y) / 2, (border.Z + dir.Z) / 2);
                return (tileA, tileB);
            }
        }

        throw new ArgumentException($"坐标 ({border.X}, {border.Y}, {border.Z}) 并非有效边界坐标。");
    }

    /// <summary>
    /// 3. 获取指定主地块周围的一圈 6 个边界的坐标
    /// </summary>
    public static List<HexCoord> GetBordersOfTile(HexCoord tile)
    {
        var borders = new List<HexCoord>(6);
        foreach (var neighbor in HexUtility.GetAllNeighbors(tile))
        {
            borders.Add(GetBorderCoord(tile, neighbor));
        }
        return borders;
    }
    

}