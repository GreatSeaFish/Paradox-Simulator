// res://View/Components/FundsDisplayBar.cs
using Godot;
using ParadoxSimulator.Simulation;

public partial class FundsDisplayBar : HBoxContainer
{
    private Label _amountLabel = null!;
    private Label _changeLabel = null!;

    public override void _Ready()
    {
        // 1. 获取自身的文本节点引用（根据原本 funds_bar 下的层级对齐）
        _amountLabel = GetNode<Label>("Amount");
        _changeLabel = GetNode<Label>("Change");

        // 2. 挂载到逻辑状态仓库的资金事件上，不再需要外层类来指挥
        if (CoreHost.WorldSimulationState != null)
        {
            CoreHost.WorldSimulationState.OnPlayerRealtimeFundsChanged += OnLocalPlayerRealtimeFundsChanged;
            CoreHost.WorldSimulationState.OnPlayerMonthlyExpectedChanged += OnLocalPlayerMonthlyExpectedChanged;
            
            // 主动向 UI 对齐一次当前最新的资金状态
            CoreHost.WorldSimulationState.NotifyFundsChanged(CoreHost.LocalContext.MyPlayerId);
        }
    }

    public override void _ExitTree()
    {
        // 销毁时记得注销事件，防止内存泄漏
        if (CoreHost.WorldSimulationState != null)
        {
            CoreHost.WorldSimulationState.OnPlayerRealtimeFundsChanged -= OnLocalPlayerRealtimeFundsChanged;
            CoreHost.WorldSimulationState.OnPlayerMonthlyExpectedChanged -= OnLocalPlayerMonthlyExpectedChanged;
        }
    }

    /// <summary> 实时变动接收中心：只管更新总额 Amount </summary>
    private void OnLocalPlayerRealtimeFundsChanged(int playerId, int currentFunds)
    {
        if (playerId != CoreHost.LocalContext.MyPlayerId) return;
        if (IsInstanceValid(_amountLabel))
        {
            _amountLabel.Text = currentFunds.ToString();
        }
    }

    /// <summary> 预期/账单变动接收中心：只管更新月度盈亏预测 Change </summary>
    private void OnLocalPlayerMonthlyExpectedChanged(int playerId, int expectedFundsChange)
    {
        if (playerId != CoreHost.LocalContext.MyPlayerId) return;
        if (!IsInstanceValid(_changeLabel)) return;

        if (expectedFundsChange > 0)
        {
            _changeLabel.Text = $"+{expectedFundsChange}";
            _changeLabel.Modulate = Colors.LimeGreen; // 预期盈利显绿色
        }
        else if (expectedFundsChange < 0)
        {
            _changeLabel.Text = $"{expectedFundsChange}";
            _changeLabel.Modulate = Colors.Red;       // 预期赤字显红色
        }
        else
        {
            _changeLabel.Text = "+0";
            _changeLabel.Modulate = Colors.White;     // 收支平衡显白色
        }
    }
}