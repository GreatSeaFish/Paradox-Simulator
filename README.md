# 临时技术文档

本项目采用**分层架构**设计，核心逻辑基于帧同步（Lockstep）机制实现，以确保客户端之间的数据强一致性。

## 1. 核心分层架构

整个项目在结构上主要分为两层：

- **Simulation 层（模拟层）**：负责纯粹的游戏逻辑演算、状态管理与确定性计算。
    
- **View 层（表现层）**：负责将 Simulation 层的数据渲染并展现给玩家，同时接收玩家的输入操作并将其转化为交互事件。
    

## 2. 状态管理 (State)

所有客户端的运行状态均存放在 `State` 文件夹中。

### `WorldSimulationState.cs`

该类是游戏世界的**确定性状态动态模型**，承载了游戏内的所有核心动态实时数据。

- **核心职责**：类似于桌游中的“棋盘”与“玩家面板”。它不负责逻辑演算，只负责数据的存储、提供主动读取接口、以及派发数据变更事件。
    
- **包含数据**：地图数据、世界时间、地块归属、玩家资金、外交状态（规划中）等。
    
- **交互机制**：
    
    - **Systems & Commands**：通过调用该类内部的管理方法直接调整数据。
        
    - **View 层**：通过主动读取该类的数据进行画面渲染，或通过订阅该类的事件（Event）实现响应式的UI和特效更新。
        

## 3. 逻辑演算与驱动 (Systems & Commands)

Simulation 层的运转由 Systems 和 Commands 共同驱动。

### Systems（系统层）

Systems 是一套由帧心跳（Tick）驱动的自动数据演算机器。

- **核心职责**：负责管理游戏宏观进程的推进、时间齿轮的转动以及周期性的数值结算。
    
- **运转机制**：每当新的一帧驱动到来，Systems 会自动读取 `WorldSimulationState` 中的当前设定和数据，进行确定性的物理或数值演算，并将演算后的新数据重新写回 `WorldSimulationState`。
    

### Commands（指令层）

Commands 是网络输入与本地状态之间的桥梁。

- **核心职责**：负责接收并解析网络层（服务器）传来的网络指令包。
    
- **运转机制**：将指令包拆解后，直接在 `WorldSimulationState` 中修改对应的设定或玩家状态，为 Systems 下一帧的演算提供输入依据。
    

## 4. 网络同步机制（帧同步）

项目采用标准的帧同步（Lockstep）架构，确保所有客户端在相同的输入下演算出完全一致的结果。

### 同步工作流

1. **客户端采集与发送**：客户端以 **50ms** 为周期，收集本地玩家的操作输入，并作为指令发送给服务端。
    
2. **服务端打包与广播**：服务端瞬时收集所有玩家的指令，不进行逻辑演算，仅将其打包，并同样以 **50ms** 的周期向所有客户端广播这个“指令包”。
    
3. **客户端驱动与演算**：
    
    - 客户端每接收到一个指令包，游戏世界便**向前推进一帧（Tick）**。
        
    - 首先由 **Command** 根据指令包内容，修改 `WorldSimulationState` 中的玩家设定。
        
    - 随后 **Systems** 自动根据更新后的 `WorldSimulationState` 设定进行逻辑演算，更新世界模型。
        

```
[玩家输入] -> (客户端发送: 50ms) -> [服务端打包] -> (广播指令包: 50ms)
                                                        |
                                                        v
                                          [客户端接收] -> 推进一帧 (Tick)
                                                        |
                                        +---------------+---------------+
                                        |                               |
                                        v                               v
                                  [Commands]                      [Systems]
                            修改 WorldSimulationState 设定     依据新设定自动演算世界数据

---

# 新功能开发标准工作流 (SOP)

## 第一阶段：逻辑层 —— 数据与状态定义 (State)

**核心原则**：所有影响游戏进程的数据必须是纯数据（无 Godot 节点），且具备绝对的确定性。

1. **定义数据结构** `[WorldSimulationState.cs]`
    
    - 在文件底部创建纯数据类/结构体（如 `UnitBuildTask`, `MilitaryUnit`）。
        
2. **注册核心数据容器** `[WorldSimulationState.cs]`
    
    - 在状态机中添加对应的 `Dictionary` 或 `List` 作为全局数据容器。
        
3. **声明表现层驱动事件** `[WorldSimulationState.cs]`
    
    - 定义 `event Action<T>` 事件，专供 View 层监听（如 `OnUnitSpawned`）。
        
4. **封装数据变更方法** `[WorldSimulationState.cs]`
    
    - 编写供 System 调用的方法，在方法内**同时修改数据字典**并**Invoke 触发事件**。
        
5. **清理脏数据** `[GameStateSystem.cs]`
    
    - 在 `StartGame()` 方法的清理环节中，调用新字典的 `.Clear()`，确保每次开局状态纯净。
        

## 第二阶段：逻辑层 —— 指令与网络通信 (Command)

**核心原则**：客户端只发操作意图，不直接改状态；由服务端广播后，各端统一步调执行。

1. **封装客户端发送** `[ClientCommandSender.cs]`
    
    - 编写 `SendXxxCommand()`，分配新的 `InputType` ID，打包序列化并发送给服务端。
        
2. **注册路由映射** `[CommandFactory.cs]`
    
    - 在 `Create()` 的 switch 语句中，将对应的 `InputType` 映射到具体的指令类。
        
3. **实现确定性执行逻辑** `[Commands/XxxCommand.cs]`
    
    - 新建指令类实现 `IGameCommand` 接口。
        
    - 在 `Execute()` 中编写严格的**规则校验**（所有前置条件：归属权、资金、防止连点或重叠状态）。
        
    - 校验通过后，执行核心状态变更。
        

## 第三阶段：逻辑层 —— 系统驱动与结算 (System)

**核心原则**：处理随时间流逝自动发生的状态变化。

1. **设计结算/Tick机制** `[如 SettlementSystem.cs]`
    
    - 在对应的系统 `Execute` 循环中，遍历新加的数据容器。
        
    - 处理倒计时、资源随时间增减等逻辑。
        
    - 当条件满足时，调用第一阶段封装好的“数据变更方法”（从而触发渲染层事件）。
        

## 第四阶段：表现层 —— UI 与交互响应 (View - UI)

**核心原则**：UI 只反映当前逻辑层的 State，不能自己存核心状态；用户点击只负责发 Command。

1. **扩展复用组件/面板** `[Components/XxxPanel.cs]`
    
    - 根据 `WorldSimulationState` 中的最新数据，决定 UI 的显示状态（改字、禁用/启用按钮、显示进度条）。
        
    - 如有需要，引入内部状态机（如 `ActionMode` 枚举）区分同一个按钮的不同功能。
        
2. **绑定交互与防抖** `[Components/XxxPanel.cs]`
    
    - 按钮点击事件绑定对应的 `CoreHost.CommandSender.SendXxxCommand()`。
        
    - **必做**：点击后立即禁用按钮/更改文案（防连点），等待帧同步网络包回来后，由 UI 刷新机制自然解锁。
        

## 第五阶段：表现层 —— 场景渲染与动画 (View - Render)

**核心原则**：被动接收逻辑层的事件通知，只负责“画”，绝对不干涉游戏逻辑。

1. **预加载与节点声明** `[MainGameView.cs]`
    
    - 使用 `GD.Load<PackedScene>` 预加载相关的预制体（如模型、特效）。
        
    - 声明容器节点引用（如挂载对象的根节点）以及用于追踪已生成实体的 `Dictionary<Id, Node2D>`。
        
2. **事件监听与注销** `[MainGameView.cs]`
    
    - 在 `_Ready()` 中挂载逻辑层的事件：`CoreHost.WorldSimulationState.OnXxx += OnXxxSync;`
        
    - **必做**：在 `_ExitTree()` 中注销对应事件 `-=`，防止内存泄漏和空指针。
        
3. **编写实例化逻辑** `[MainGameView.cs]`
    
    - 实现事件回调方法 `OnXxxSync()`。
        
    - 在方法内实例化预制体，坐标转换（网格转世界坐标），装配材质/颜色/文字。
        
4. **每帧动态表现** `[MainGameView.cs]`
    
    - 在 `_Process(double delta)` 中，遍历逻辑层 State 里的容器，动态更新地图悬浮字倒计时、进度条、粒子等与帧率相关但不影响游戏规则的平滑表现。
```
