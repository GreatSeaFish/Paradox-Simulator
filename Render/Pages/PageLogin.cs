using Godot;
using System;
using ParadoxSimulator.Core;
using Shared.Protocol;

public partial class PageLogin : Control
{
    private LineEdit _ipInput = null!;
    private LineEdit _nickNameInput = null!; // 【新增】昵称输入框
    private Button _joinButton = null!;
    private RichTextLabel _stateLabel = null!;

    public override void _Ready()
    {
        _joinButton = GetNode<Button>("JoinContainer/JoinButton");
        _ipInput = GetNode<LineEdit>("JoinContainer/IPandPortInput");
        _nickNameInput = GetNode<LineEdit>("JoinContainer/NickNameInput"); // 根据你的 tscn 获取
        _stateLabel = GetNode<RichTextLabel>("StateRichTextLabel");

        _joinButton.Pressed += OnJoinButtonPressed;
    }

    private void OnJoinButtonPressed()
    {
        string rawInput = _ipInput.Text.Trim();
        string nickname = _nickNameInput.Text.Trim();

        if (string.IsNullOrEmpty(rawInput) || string.IsNullOrEmpty(nickname))
        {
            _stateLabel.Text = "错误：IP 和 昵称 不能为空！";
            return;
        }

        try
        {
            string[] parts = rawInput.Split(':');
            string ip = parts[0];
            int port = parts.Length > 1 && int.TryParse(parts[1], out int parsedPort) ? parsedPort : 5721;

            _stateLabel.Text = $"正在尝试连接到 {ip}:{port}...";
            
            // 禁用按钮防多次点击
            _joinButton.Disabled = true;

            // 【核心修改】注册网络回调（确保 CoreHost 的 _Process 轮询在主线程触发这些回调）
            CoreHost.NetworkManager.OnInitReceived += OnConnectedAndInit;
            CoreHost.NetworkManager.OnLobbySyncReceived += OnFirstLobbySync;

            LocalClientInfo.MyNickname = nickname; // 保存本地昵称
            CoreHost.ConnectToServer(ip, port);
        }
        catch (Exception ex)
        {
            _stateLabel.Text = $"连接错误: {ex.Message}";
            _joinButton.Disabled = false;
        }
    }

    // 当收到服务器分配的 ID 时触发
    private void OnConnectedAndInit(int myId)
    {
        _stateLabel.Text += $"\n连接成功！我的ID是: {myId}。正在加入大厅...";
        
        // 构建并发送加入大厅请求
        var joinPacket = new ClientJoinLobbyPacket { Nickname = LocalClientInfo.MyNickname };
        CoreHost.NetworkManager.SendPacket(PacketType.ClientJoinLobby, joinPacket);
    }

    // 当收到服务器下发的第一份大厅数据时触发
    private void OnFirstLobbySync(System.Collections.Generic.List<LobbyPlayerInfo> players)
    {
        // 收到大厅数据，说明成功挤进房间了，注销事件，防止切场景后内存泄漏或重复触发
        CoreHost.NetworkManager.OnInitReceived -= OnConnectedAndInit;
        CoreHost.NetworkManager.OnLobbySyncReceived -= OnFirstLobbySync;
        
        // 正式跳转到大厅页面
        GetTree().ChangeSceneToFile("res://Render/Pages/page_lobby.tscn");
    }
}