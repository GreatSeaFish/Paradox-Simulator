namespace Shared.Protocol
{
    /// <summary>
    /// 客户端与服务端交互的物理指令枚举
    /// 底层指定为 short (2 Bytes)，最大支持 65535 种操作
    /// </summary>
    public enum CommandType : short
    {
        None = 0,
        Move = 1,
        TimeSpeedControl = 2,
        Colonize = 3,
        BuildUnit = 4,
        UnitMove = 5, // 新增：部队移动指令
        // 预留区间示例：
        // DiplomacyStart = 1000,
        // TradeStart = 2000,
    }
}