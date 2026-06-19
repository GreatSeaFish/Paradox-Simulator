using System.Collections.Generic;
using Shared.Protocol; // 【新增】引入协议命名空间

namespace ParadoxSimulator.Core
{
    public static class LocalClientInfo
    {
        // 本地玩家 ID
        public static int MyPlayerId { get; set; } = -1;
        
        // 本地玩家的昵称
        public static string MyNickname { get; set; } = string.Empty;

        // 【新增】保存当前大厅内所有玩家的最新状态
        public static List<LobbyPlayerInfo> LobbyPlayers { get; set; } = new List<LobbyPlayerInfo>();

        public static int GetMyPlayerId()
        {
            return MyPlayerId;
        }
    }
}