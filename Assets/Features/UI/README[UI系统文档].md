# UI 界面模块（菜单 + 打歌）

## 概述

菜单侧（选歌页 / 设置弹层）与打歌侧（HUD / 暂停 / 结算）的全部界面。UGUI + MVP：View 是哑巴 MonoBehaviour，逻辑全在纯 C# Presenter；装配走 Zenject。

- **位置**：`Assets/Features/UI/Scripts/`（本模块全部代码，`Temp/` 为临时脚本）。
- **装配分工**：菜单侧由本模块 `MenuSceneInstaller` 装配；打歌侧 Presenter 由 GamePlay 的 `TrackPlayingInstaller` 装配（跨模块接线点）。
- **场景**：MenuScene ↔ TrackPlayingScene 双场景循环；结算不是独立场景，是打歌场景内的阶段（曲终出面板）。
- **文档定位**：本 README 讲结构与机制；拍板决策与实施过程在《主菜单UI框架设计》，是 AI 工作记录。

## MVP 的应用

### 三层约定

| 层 | 形态 | 职责 |
|---|---|---|
| View | MonoBehaviour（prefab / 场景对象上） | 只持序列化控件引用；对外上抛"用户输入事件"、暴露 `SetXxx` 下推口。零逻辑、零服务访问 |
| Presenter | 纯 C#（无 MonoBehaviour） | 持服务与浏览状态；订阅 View 事件做决定，再命令 View。规则（难度回退 / 边界钳制 / 节流）唯一住这层 |
| 装配 | Zenject Installer | View 由 Installer `[SerializeField]` 手拖，Presenter 经 `WithArguments(view)` 绑定 |

**数据流一句话**：服务数据只流向 Presenter，Presenter 命令 View 显示；用户输入从 View 上抛回 Presenter。面板之间零引用——横向通信走导航器或服务层。

### 生命周期

- 全部 Presenter 场景级 `AsSingle` + `IInitializable`（首帧前初始化：下推初始数据、订阅事件）。
- 需要每帧的走 `ITickable`：`MenuNavigator`（ESC 轮询）、`SongSelectPresenter`（方向键轮询）、`GameHudPresenter`（数据下推）、`PausePanelPresenter`（阶段 → UI 形态）。
- 设置弹层 = 预实例化：面板常驻菜单场景、默认隐藏；`SettingsPresenter` 在 `Initialize` 时向 `MenuNavigator` 注册，打开 / 关闭唯一经导航器。

### 交互组件选择

- 纯点击交互（关闭 / 重试 / 返回 / 开始等）用 Unity Button，`Awake` 里接 `onClick` 上抛，不在 Inspector 挂回调。
- 需要选中态 / 状态表现的（歌曲格 `SongCellView`、难度按钮 `DifficultyButtonView`）不用 Button 组件，自定义 `IPointerClickHandler` + 每帧向目标态平滑收敛。

## 接入的系统

| 服务 | 提供方 | UI 消费点 | 用途 |
|---|---|---|---|
| `TrackCollection` | ProjectContext | `SongSelectPresenter` | 曲目列表源（全部 / 官方 / 自制 / 官方分类查询） |
| `PlaySessionService` | ProjectContext（跨场景） | `SongSelectPresenter` 写 / 打歌侧读 | 本局启动参数（选曲 / AutoPlay） |
| `ScoreArchive` | ProjectContext | `SongSelectPresenter` / `ResultPresenter` | 最佳成绩读；结算写档 |
| `PlayerConfigStore` | ProjectContext | `SettingsPresenter` | 全局流速 / 偏移读写 |
| `InputService` | ProjectContext | `MenuNavigator` / `SongSelectPresenter` / `SettingsPresenter` | ESC 与方向键轮询；键位重绑与键名 |
| `ScoringSystem` | 打歌场景容器 | `GameHudPresenter` / `ResultPresenter` | 分数 / 连击 / 判定数据 |
| `TrackController` | 打歌场景容器 | `GameHudPresenter` / `PausePanelPresenter` / `ResultPresenter` | 播放时间 / 阶段 / 恢复倒数 / 曲终事件 |
| `MenuNavigator` | 菜单场景容器（本模块） | `SongSelectPresenter` / `SettingsPresenter` | 场景跳转 + 弹层开合 |

（各服务自身的生命周期与持久化细节见对应模块 README，如 Input 模块。）

## 特殊处理

### 轮盘（SongSelectView）

- **单一光标浮点**：View 唯一动画状态 = `cursor` 浮点（单位 = 曲目索引空间）+ 整数目标；切歌只改目标值。所有格子动画 = 同一光标每帧被采样的结果，不是 N 个格子各自补间——快速连切天然平滑。
- **池 = 槽位表条数 + 1**：槽位表 `{X, Y, Scale, Alpha}` 序列化在 View 上（可增删 / 非对称）。
- **换绑不变量**：最外槽 Alpha 恒 0（回收线）；实例只在完全透明时换绑——可见格永不换内容。
- **方向相关缓动**：贴中心两段（|rel| ≤ 1）的 X / Scale 按"进中心 / 出中心"取不同指数曲线（快进快出）；Y / Alpha 线性。
- 边界无环绕；无歌可绑的槽位停用；位置指示条随 cursor 纯显示。

### 弹层导航（MenuNavigator）

- 场景跳转 + 弹层开合 + **ESC 统一分发**（有弹层先关弹层）；重绑捕获中的 ESC 由 InputService 自行抑制，消费者无需特判。
- `IsPopupOpen`：弹层打开时选歌页所有输入入口（点格 / 滚轮 / 方向键 / 开始 / AutoPlay 开关）统一屏蔽。

### 键位重绑 UI（SettingsPresenter）

- 点行 → 显示"请按键"提示层 → `InputService.StartRebind`（回调式）；完成 / 取消同一出口：收提示 + 刷新该行。捕获中 UI 入口由 `IsRebinding` 守卫。
- `RESET KEYS`：清全部覆盖（即时落盘）并刷新。
- 流速 / 偏移：单击步进 + 边界钳制（步进与边界常量在 `SettingsPresenter`），即时写 store 内存；关闭弹层时检查点落盘。

### 输入节流（SongSelectPresenter）

- 滚轮（View 指针事件上抛）与键盘方向键（Presenter 轮询）走同一入口，共用 0.08s 节流（unscaled），防平滑滚轮连发一滚多格。

### AutoPlay 模式（选歌页 / 打歌侧）

- 选歌页勾选框写 `PlaySessionService.AutoPlay`（会话级，启动默认关；返回菜单保留上次选择）；弹层打开时屏蔽切换。
- `TrackController.Start` 把值快照为本局 `IsAutoPlay`，整局不变：局内走 `NoteJudge.AutoPlayUpdate`，不采集轨道输入。
- 结算不写档、不记首次 FC，只显示曲目信息 + `AUTO PLAY`；打歌 HUD 显示 AUTO 标识。

### HUD 下推与阶段驱动（打歌侧）

- HUD：分数 / 连击 / AutoPlay 标识缓存比较、值变化才下推；进度只前进不后退（曲终时间源冻结）。
- 暂停 / 结算的 UI 形态不互相猜，统一由 `TrackController` 驱动：暂停面板每帧把 Phase 映射成形态（变化才刷）；结算订阅 `TrackEnded` 事件。

## 类清单

| 类 | 职责 |
|---|---|
| `SongSelectView` / `SongCellView` / `DifficultyButtonView` / `DifficultyState` | 选歌页视图层：轮盘与采样、单曲格、难度按钮、难度态下推数据 |
| `SongSelectPresenter` | 选歌页大脑：光标 / 难度状态与规则、节流、选曲写服务 |
| `AutoPlayToggleView` | 选歌页 AutoPlay 勾选框（点击上抛 + 勾选态下推） |
| `MenuNavigator` | 导航：场景跳转 + 弹层开合 + ESC 分发 |
| `MenuSceneInstaller` | MenuScene 的 Zenject 装配入口 |
| `SettingsPanelView` / `KeybindRowView` / `SettingsPresenter` | 设置弹层：视图 + 大脑（步进 / 重绑 / 落盘） |
| `GameHudView` / `GameHudPresenter` | 打歌 HUD（分数 / 连击 / 进度 / AutoPlay 标识） |
| `PausePanelView` / `PausePanelPresenter` | 暂停面板（阶段驱动 + 恢复倒数） |
| `ResultPanelView` / `ResultPresenter` | 结算面板（正式成绩写档；AutoPlay 不写档，只显示 AUTO PLAY） |
| `Temp/TempSceneJump` | 临时互跳验证脚本（正式流程接入后删除） |

## 相关文件与文档

- 设计记录：[主菜单UI框架设计.md](../../../_Project/Docs/主菜单UI框架设计.md)——拍板决策与实施过程（AI 工作记录）；遇到"为什么这么设计"翻这里。
- 输入细节：[Input 模块 README](<../Input/Scripts/README[输入系统文档].md>)——动作地图、重绑语义、ESC 抑制。
- 打歌侧装配：`Assets/Features/GamePlay/Scripts/Installer/TrackPlayingInstaller.cs`。
