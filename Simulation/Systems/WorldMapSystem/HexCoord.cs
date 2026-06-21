using System;


namespace ParadoxSimulator.Simulation.Systems.WorldMapSystem;
/// <summary>
/// 三维六边形坐标系结构（用于替代 Vector3I 以保持纯 C# 逻辑）
/// </summary>
public struct HexCoord : IEquatable<HexCoord>
{
    public int X;
    public int Y;
    public int Z;

    public HexCoord(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    // 两个六边形之间的距离（纯整数逻辑）
    public static int Distance(HexCoord a, HexCoord b)
    {
        return Math.Max(Math.Abs(a.X - b.X), Math.Max(Math.Abs(a.Y - b.Y), Math.Abs(a.Z - b.Z)));
    }

    public static HexCoord operator +(HexCoord a, HexCoord b) => new HexCoord(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static bool operator ==(HexCoord a, HexCoord b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    public static bool operator !=(HexCoord a, HexCoord b) => !(a == b);

    public override bool Equals(object obj) => obj is HexCoord other && Equals(other);
    public bool Equals(HexCoord other) => this == other;

    public override int GetHashCode()
    {
        // 简单的 HashCode 组合，用于字典查找（查找是确定性的，只要不依赖字典的迭代顺序即可）
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + X;
            hash = hash * 31 + Y;
            hash = hash * 31 + Z;
            return hash;
        }
    }
}