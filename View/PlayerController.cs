using Godot;
using System;
using System.Linq;

public partial class PlayerController : Node
{
    private TabBar _timeFlowRateTab = null!;
    private Label _gameCalendarLabel = null!;
    private Label _fundsAmountLabel = null!;
    private Label _fundsChangeLabel = null!;

    public override void _Ready()
    {
        _timeFlowRateTab = GetNode<TabBar>("%TimeFlowRate");
        _gameCalendarLabel = GetNode<Label>("%GameCalendar");
        _fundsAmountLabel = GetNode<Label>("Hud/Root/TopBar/FundsBar/Amount");
        _fundsChangeLabel = GetNode<Label>("Hud/Root/TopBar/FundsBar/Change");
        
        _timeFlowRateTab.TabClicked += OnTimeTabClicked;

        // 订阅逻辑层的全新分流资金变动事件
        if (CoreHost.WorldSimulationState != null)
        {
            CoreHost.WorldSimulationState.OnPlayerRealtimeFundsChanged += OnLocalPlayerRealtimeFundsChanged;
            CoreHost.WorldSimulationState.OnPlayerMonthlyExpectedChanged += OnLocalPlayerMonthlyExpectedChanged;
            
            // 初始化主动对齐一次
            CoreHost.WorldSimulationState.NotifyFundsChanged(CoreHost.LocalContext.MyPlayerId);
        }
        
        if (CoreHost.TimeSystem != null)
        {
            CoreHost.TimeSystem.OnSpeedChanged += OnSpeedChangedSync;
            // 【核心修改】：UI 直接订阅状态层提供的时间变更
            CoreHost.WorldSimulationState.OnDateChanged += OnDateChangedSync;
            UpdateCalendarText(CoreHost.WorldSimulationState.CurrentDate);
        }
        
        _timeFlowRateTab.CurrentTab = 0;
    }

    /// <summary>
    /// 实时变动接收中心：只管更新总额 Amount
    /// </summary>
    private void OnLocalPlayerRealtimeFundsChanged(int playerId, int currentFunds)
    {
        if (playerId != CoreHost.LocalContext.MyPlayerId) return;
        
        if (IsInstanceValid(_fundsAmountLabel))
        {
            _fundsAmountLabel.Text = currentFunds.ToString();
        }
    }

    /// <summary>
    /// 预期/账单变动接收中心：只管更新月度盈亏预测 Change
    /// </summary>
    private void OnLocalPlayerMonthlyExpectedChanged(int playerId, int expectedFundsChange)
    {
        if (playerId != CoreHost.LocalContext.MyPlayerId) return;

        if (IsInstanceValid(_fundsChangeLabel))
        {
            if (expectedFundsChange > 0)
            {
                _fundsChangeLabel.Text = $"+{expectedFundsChange}";
                _fundsChangeLabel.Modulate = Colors.LimeGreen; // 预期盈利显绿色
            }
            else if (expectedFundsChange < 0)
            {
                _fundsChangeLabel.Text = $"{expectedFundsChange}"; // 自带负号
                _fundsChangeLabel.Modulate = Colors.Red;       // 预期赤字显红色
            }
            else
            {
                _fundsChangeLabel.Text = "+0";
                _fundsChangeLabel.Modulate = Colors.White;     // 收支平衡显白色
            }
        }
    }
    
    public override void _ExitTree()
    {
        if (CoreHost.WorldSimulationState != null)
        {
            CoreHost.WorldSimulationState.OnPlayerRealtimeFundsChanged -= OnLocalPlayerRealtimeFundsChanged;
            CoreHost.WorldSimulationState.OnPlayerMonthlyExpectedChanged -= OnLocalPlayerMonthlyExpectedChanged;
        }

        if (CoreHost.TimeSystem != null)
        {
            CoreHost.TimeSystem.OnSpeedChanged -= OnSpeedChangedSync;
            // 【核心修改】：UI 直接订阅状态层提供的时间变更
            CoreHost.WorldSimulationState.OnDateChanged += OnDateChangedSync;
        }
    }

    private void OnTimeTabClicked(long tabIndex)
    {
        CoreHost.CommandSender.SendTimeSpeedCommand((int)tabIndex);
    }

    private void OnSpeedChangedSync(int newSpeedLevel)
    {
        if (_timeFlowRateTab.CurrentTab != newSpeedLevel)
        {
            _timeFlowRateTab.CurrentTab = newSpeedLevel;
        }
    }

    private void OnDateChangedSync(DateTime newDate)
    {
        UpdateCalendarText(newDate);
    }

    private void UpdateCalendarText(DateTime date)
    {
        _gameCalendarLabel.Text = $"第{date.Year}年{date.Month}月{date.Day}日";
    }
}