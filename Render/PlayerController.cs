using Godot;
using System;

public partial class PlayerController : Node
{
    private TabBar _timeFlowRateTab = null!;
    
    // 【新增】日历文本标签的引用
    private Label _gameCalendarLabel = null!; 

    public override void _Ready()
    {
        _timeFlowRateTab = GetNode<TabBar>("Hud/CalendarContainer/TimeFlowRate");
        
        // 【新增】通过相对路径获取 tscn 场景中的 GameCalendar 节点
        _gameCalendarLabel = GetNode<Label>("Hud/CalendarContainer/GameCalendar"); 

        _timeFlowRateTab.TabClicked += OnTimeTabClicked;

        if (CoreHost.GameTime != null)
        {
            CoreHost.GameTime.OnSpeedChanged += OnSpeedChangedSync;
            
            // 【新增】订阅逻辑层确定性的日期变更事件
            CoreHost.GameTime.OnDateChanged += OnDateChangedSync; 
            
            // 【新增】初始化时，立刻根据逻辑层的初始时间刷新一次 UI 文本（显示 第1年4月1日）
            UpdateCalendarText(CoreHost.GameTime.CurrentDate);
        }
        
        _timeFlowRateTab.CurrentTab = 0; 
    }

    public override void _ExitTree()
    {
        if (CoreHost.GameTime != null)
        {
            CoreHost.GameTime.OnSpeedChanged -= OnSpeedChangedSync;
            
            // 【新增】安全注销事件，防止切场景内存泄漏
            CoreHost.GameTime.OnDateChanged -= OnDateChangedSync; 
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

    // 【新增】收到逻辑层天数增加时的回调方法
    private void OnDateChangedSync(DateTime newDate)
    {
        UpdateCalendarText(newDate);
    }

    // 【新增】封装的统一文本格式化方法
    private void UpdateCalendarText(DateTime date)
    {
        // 拼接成符合你要求的 "第*年*月*日" 格式
        _gameCalendarLabel.Text = $"第{date.Year}年{date.Month}月{date.Day}日";
    }
}