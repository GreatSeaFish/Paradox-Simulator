using FixedMath.NET; // 确保引入了 FixedMath.NET 的命名空间

namespace Shared.Math;

public struct FixVector2
{
    public Fix64 X;
    public Fix64 Y;

    public FixVector2(Fix64 x, Fix64 y)
    {
        X = x;
        Y = y;
    }

    // 常用快捷属性
    public static FixVector2 Zero => new FixVector2(Fix64.Zero, Fix64.Zero);

    // 简易的加法操作，用于后续位移计算
    public static FixVector2 operator +(FixVector2 a, FixVector2 b)
    {
        return new FixVector2(a.X + b.X, a.Y + b.Y);
    }
    
    public static FixVector2 operator *(FixVector2 a, Fix64 b)
    {
        return new FixVector2(a.X * b, a.Y * b);
    }

    public override string ToString() => $"({(float)X}, {(float)Y})";
}