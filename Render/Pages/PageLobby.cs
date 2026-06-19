using Godot;
using System.Collections.Generic;
using ParadoxSimulator.Core;
using Shared.Protocol;
using System.Linq;

public partial class PageLobby : Control
{
    private VBoxContainer _playerListContainer = null!;
    private OptionButton _slotOption = null!;
    private OptionButton _colorOption = null!;
    private Button _readyButton = null!;

    // 预设的 8 种颜色
    private readonly string[] _colorNames = { "红色", "蓝色", "橙色", "绿色", "黄色", "紫色", "粉色", "淡蓝" };
    private readonly Godot.Color[] _colorValues = { 
        Colors.Red, Colors.Blue, Colors.Orange, Colors.Green, 
        Colors.Yellow, Colors.Purple, Colors.Pink, Colors.LightBlue 
    };

    public override void _Ready()
    {
        // 绑定节点 (请根据你的场景树路径调整)
        _playerListContainer = GetNode<VBoxContainer>("MainLayout/LeftPanel/PlayerListScroll/PlayerListContainer");
        _slotOption = GetNode<OptionButton>("MainLayout/RightPanel/SlotSetting/SlotOption");
        _colorOption = GetNode<OptionButton>("MainLayout/RightPanel/ColorSetting/ColorOption");
        _readyButton = GetNode<Button>("MainLayout/RightPanel/ReadyButton");

        // 初始化颜色下拉框
        _colorOption.Clear();
        for (int i = 0; i < _colorNames.Length; i++)
        {
            _colorOption.AddItem(_colorNames[i], i);
        }

        // 绑定 UI 交互事件
        _slotOption.ItemSelected += OnSlotOrColorChanged;
        _colorOption.ItemSelected += OnSlotOrColorChanged;
        _readyButton.Pressed += OnReadyButtonPressed;

        // 绑定网络事件
        CoreHost.NetworkManager.OnLobbySyncReceived += UpdateLobbyUI;
        CoreHost.NetworkManager.OnGameStartReceived += OnGameStart;

        // 页面加载完毕时，先拿缓存的数据刷一次 UI
        UpdateLobbyUI(LocalClientInfo.LobbyPlayers);
    }

    public override void _ExitTree()
    {
        // 退出节点时一定要注销事件，防止报错
        if (CoreHost.NetworkManager != null)
        {
            CoreHost.NetworkManager.OnLobbySyncReceived -= UpdateLobbyUI;
            CoreHost.NetworkManager.OnGameStartReceived -= OnGameStart;
        }
    }

    // ================== 网络回调 ==================

    private void UpdateLobbyUI(List<LobbyPlayerInfo> players)
    {
        // 1. 清空现有的玩家列表 UI
        foreach (Node child in _playerListContainer.GetChildren())
        {
            child.QueueFree();
        }

        int myId = LocalClientInfo.MyPlayerId;
        LobbyPlayerInfo? myInfo = null;

        // 2. 重新生成玩家列表
        foreach (var p in players)
        {
            if (p.PlayerId == myId) myInfo = p;

            // 动态创建一个简单的水平容器显示玩家信息
            var hbox = new HBoxContainer();
            
            var colorRect = new ColorRect { CustomMinimumSize = new Vector2(20, 20), Color = _colorValues[p.ColorId] };
            var nameLabel = new Label { Text = $"  位置 {p.SlotId + 1} | {p.Nickname} {(p.PlayerId == myId ? "(我)" : "")}" };
            var readyLabel = new Label { 
                Text = p.IsReady ? " [已准备]" : " [未准备]", 
                Modulate = p.IsReady ? Colors.Green : Colors.Gray 
            };

            hbox.AddChild(colorRect);
            hbox.AddChild(nameLabel);
            hbox.AddChild(readyLabel);
            _playerListContainer.AddChild(hbox);
        }

        // 3. 更新我自己的下拉框状态与准备按钮
        if (myInfo != null)
        {
            // 暂时解除事件绑定，防止代码赋值触发 ItemSelected 发送无效的网络包
            _slotOption.ItemSelected -= OnSlotOrColorChanged;
            _colorOption.ItemSelected -= OnSlotOrColorChanged;

            _slotOption.Selected = myInfo.SlotId;
            _colorOption.Selected = myInfo.ColorId;
            _readyButton.Text = myInfo.IsReady ? "取消准备" : "准备";

            // 恢复事件绑定
            _slotOption.ItemSelected += OnSlotOrColorChanged;
            _colorOption.ItemSelected += OnSlotOrColorChanged;

            // 4. 互斥逻辑：灰化别人已经选过的位置和颜色
            for (int i = 0; i < 8; i++)
            {
                bool slotTaken = players.Any(p => p.PlayerId != myId && p.SlotId == i);
                _slotOption.SetItemDisabled(i, slotTaken);

                bool colorTaken = players.Any(p => p.PlayerId != myId && p.ColorId == i);
                _colorOption.SetItemDisabled(i, colorTaken);
            }
        }
    }

    private void OnGameStart()
    {
        // 切入游戏渲染场景
        GetTree().ChangeSceneToFile("res://Render/WorldRender.tscn");
    }

    // ================== UI 操作事件 ==================

    private void OnSlotOrColorChanged(long index)
    {
        // 发送更新请求
        var updatePacket = new ClientUpdateSlotColorPacket
        {
            SlotId = _slotOption.GetSelectedId(),
            ColorId = _colorOption.GetSelectedId()
        };
        CoreHost.NetworkManager.SendPacket(PacketType.ClientUpdateSlotColor, updatePacket);
    }

    private void OnReadyButtonPressed()
    {
        // 本地获取当前状态并取反
        var myInfo = LocalClientInfo.LobbyPlayers.FirstOrDefault(p => p.PlayerId == LocalClientInfo.MyPlayerId);
        if (myInfo != null)
        {
            bool nextState = !myInfo.IsReady;
            var readyPacket = new ClientToggleReadyPacket { IsReady = nextState };
            CoreHost.NetworkManager.SendPacket(PacketType.ClientToggleReady, readyPacket);
        }
    }
}