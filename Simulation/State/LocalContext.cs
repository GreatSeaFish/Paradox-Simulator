using System.Collections.Generic;
using Shared.Protocol;
using Shared.Math;

namespace ParadoxSimulator.Core.GameData;

/// <summary>
/// 客户端本地独立上下文 (不参与帧同步逻辑)
/// </summary>
public class LocalContext
{
    // 本地身份信息
    public int MyPlayerId { get; set; } = -1;
    public string MyNickname { get; set; } = string.Empty;
    
    // 大厅房间信息缓存
    public List<LobbyPlayerInfo> LobbyPlayers { get; set; } = new List<LobbyPlayerInfo>();
    
    // 采集到的本地输入方向缓存 (由渲染层写入，网络层读取发送)
    public FixVector2 LocalInputDirection { get; set; } = FixVector2.Zero;
    
    
    

    /// <summary>
    /// 设置本地输入方向缓存，供逻辑渲染解耦使用
    /// </summary>
    public void SetLocalInput(FixVector2 direction)
    {
        LocalInputDirection = direction; 
    }
}