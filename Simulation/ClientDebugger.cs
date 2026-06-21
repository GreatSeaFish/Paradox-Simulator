using System;

namespace ParadoxSimulator.Simulation;

public class ClientDebugger
{
    // 全局日志委托
    public static Action<string> LogHandler { get; set; } = Console.WriteLine;
    public static Action<string> WarningHandler { get; set; } = Console.WriteLine;

}