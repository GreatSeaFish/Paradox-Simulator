namespace Shared.Protocol
{
    // 包头
    public enum PacketType : byte
    {
        ServerInit = 0,             // 服务器分配 PeerID
        ClientJoinLobby = 1,        // 客户端发送昵称加入大厅
        ServerLobbySync = 2,        // 服务器广播全员大厅状态
        ClientUpdateSlotColor = 3,  // 客户端请求更改位置和颜色
        ClientToggleReady = 4,      // 客户端切换准备状态
        ServerGameStart = 5,        // 服务器通知全员开局
        FrameData = 6               // 游戏内的逻辑帧数据
    }
}