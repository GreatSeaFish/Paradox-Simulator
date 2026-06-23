using System;

namespace ParadoxSimulator.Simulation.State.WorldModel;

/// <summary>
/// 纯确定性的游戏内日历结构体（完全由整数运算驱动，防止多端反同步）
/// </summary>
public struct GameDateTime
{
    public int Year { get; private set; }
    public int Month { get; private set; }
    public int Day { get; private set; }

    // 经典 P 社游戏或大多数 SLG 采用每季度/每月固定天数，这里采用标准的公历简化版（不考虑闰年，每年固定 365 天）
    // 如果需要 P 社最经典的“每月固定 30 天”，直接将 DaysInMonth 修改为 return 30; 即可。
    private static readonly int[] DaysInMonths = { 0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

    public GameDateTime(int year, int month, int day)
    {
        Year = year;
        Month = Math.Clamp(month, 1, 12);
        Day = Math.Clamp(day, 1, GetDaysInMonth(Month));
    }

    private static int GetDaysInMonth(int month)
    {
        // 简化的固定月天数（可根据设计改为每月固定 30 天）
        return DaysInMonths[month];
    }

    /// <summary>
    /// 纯确定性的加天数方法，替代 AddDays
    /// </summary>
    public GameDateTime AddDays(int days)
    {
        int newYear = Year;
        int newMonth = Month;
        int newDay = Day + days;

        while (newDay > GetDaysInMonth(newMonth))
        {
            newDay -= GetDaysInMonth(newMonth);
            newMonth++;
            if (newMonth > 12)
            {
                newMonth = 1;
                newYear++;
            }
        }
        return new GameDateTime(newYear, newMonth, newDay);
    }

    // 方便 UI 层直接打印字符串
    public override string ToString()
    {
        return $"{Year:D4}-{Month:D2}-{Day:D2}";
    }
}