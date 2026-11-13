# 主菜单 UI 框架设计（v1 实施版）

> **用途**：本文档是主菜单 v1 的唯一实施依据——框架决策、类设计、实施顺序、执行约束全在这里。
> **给执行 Agent**：按"五、实施计划"逐步推进。每一步只做一层，做完停下向用户讲解并等待确认；"六、执行约束"是硬性要求；遇到"九、待定事项"里的岔路，先问用户，不要自行定案。**本任务实施期间，Unity 编辑器内的操作（摆场景 / 建 prefab / 挂组件 / 导包）由用户手动执行——Agent 给出可照做的操作清单，并用文件读取（.unity 场景 / .cs 脚本）核对结果，不调用 Unity 自动化接口。**
> 状态：决策已全部拍板（2026-10-09；轮盘机制细化 2026-10-11，见决策 10 与"4.5 轮盘机制"）；步骤 0（前置打通）已完成（2026-10-10）；步骤 1（静态 UI）已完成（版式由用户自定并实装，2026-10-11）；步骤 2（View 层，轮盘）已完成（2026-10-11，经 TempWheelTest 联调通过）；步骤 3（Presenter 数据下推 + 恢复上次选曲）已完成（2026-10-12）；步骤 4a（切歌输入闭环：点格 / 滚轮 / ↑↓）已完成（2026-10-12）；步骤 4b（难度栏与难度选择：全量下推 / 就近向低回退 / 点击与 ←→ 步进闭环 / 选中置顶）已完成并实机验证（2026-10-12，决策 12）；步骤 4c（详情区下推与刷新）+ 步骤 6（开始流程：写选曲 → 载打歌场景，含去默认选曲与局内非空校验）已完成并实机验证（2026-10-12，决策 13）；前置（曲终回调 + 临时回程）已完成并实机验证（2026-10-13，决策 14）；下一步 = 步骤 7（结算侧：字段 / 按钮交互确认中；步骤 5 继续顺延）。遗留待办见"九"）。轮盘实现细节见“4.5 实现修订”。输入模块重写（设置功能前置）已完成（2026-10-15，菜单键盘输入统一；重绑实机验证挂设置弹层——决策 19）；设置弹层（含键位重绑）已完成并实机验证（2026-10-16，决策 20）。

---

## 一、v1 目标与范围

**目标：在 MenuScene 选歌 → 进 TrackPlayingScene 打 → 结算 → 重试/返回选歌，两个场景循环游玩。**

v1 包含：

- 选歌页：曲目列表（选中高亮）+ 固定详情区（封面 / 曲名 / 曲师 / 谱师 / BPM）+ 常驻难度栏（随选中曲刷新，独立于曲目选择）
- 开始按钮：写 `TrackSelectionService` → 加载 TrackPlayingScene
- 结算：局内阶段（Wwise 音乐结束回调触发），显示分数、连击等；只有"重试 / 返回选歌"两个按钮
- 前置打通：启动场景、Build Settings（两个场景都入清单）、Wwise 初始化归属

v1 明确不做（延后）：设置弹层（键位重绑 / 偏移 / 音量）、曲目试听、节拍辅助校准、难度解锁机制——详见"九、待定事项与后续版本"。

技术选型：**UGUI**。理由：逐项动效/特效自由度高、TMP 已接入、动效生态熟；UI Toolkit 更适合类编辑器的表单 UI，做音游菜单动效反而别扭。

---

## 二、现状摸底（2026-10-09）

**已有（可直接用）：**

| 内容 | 位置 |
| --- | --- |
| 曲目数据源 `TrackCollection`（聚合官谱+自制谱信息） | `Assets/Features/TrackService/` |
| 跨场景选曲服务 `TrackSelectionService`（挂 ProjectContext） | 同上 |
| 乐曲/谱面信息 `TrackInfo` / `ChartInfo`（难度 EZ/ST/HD/EX、定数、谱面文本等） | 同上 |
| 键位重绑 API + JSON 持久化（`customBindings.json`）、按键名格式化 `KeyNameDisplay` | `Assets/Common/Scripts/Input/` |
| 通用 JSON 存档单例 `SaveManager`（InputSystem 未使用它，自己写文件） | `Assets/Common/Scripts/SaveManager/` |
| 全局 DI：ProjectContext.prefab 挂 `TrackAssetInstaller` + `NoteJudgeConfigInstaller`（SO installer）；TrackPlayingScene 有 SceneContext | `Assets/Resources/`、`Assets/Features/GamePlay/` |

**缺（v1 要建）：**

- 没有菜单场景；`Assets/Features/UI/` 基本是空的（只有 `Icons/`）
- Build Settings 只挂了 SampleScene，TrackPlayingScene 都不在清单里（现在应该是直接开场景 Play 测试）
- 启动流程未定义；Wwise 初始化组件散在现有各测试场景里，归属未定

---

## 三、框架设计（已拍板）

### 3.1 场景结构

- **新建 MenuScene**，菜单所有页面在同场景内切换（不搞一页一场景）；跨场景只传"已确认的选曲"
- 理由：与现有 `TrackSelectionService` 的跨场景设计正好对上；页面级拆场景在加载成本和状态保持上都吃亏
- **场景流 = MenuScene ↔ TrackPlayingScene 双场景循环**；结算不单独开场景，是 TrackPlayingScene 内的阶段（Playing → 结算），面板做成 prefab，场景内 presenter 控制显隐
- **结算触发 = Wwise 音乐结束回调**（不看谱面结束）
- 打歌场景引入显式阶段概念（Playing / 结算），后续暂停、重开等状态复用该结构，不用面板 SetActive 互猜

### 3.2 分层

```
ProjectContext（已有，跨场景）             MenuScene（新建）
├─ TrackCollection        曲目数据源      ├─ MenuSceneInstaller (MonoInstaller)
├─ TrackSelectionService  选曲结果        └─ Canvas
└─ SettingsService        新增：偏移/音量等    ├─ SongSelectPage  选歌页（唯一常驻页面）
                                                │    ├─ 曲目列表 SongCell（prefab 池化，仅可见窗口）
                                                │    ├─ 详情区（封面 / 曲名 / 曲师 / 谱师 / BPM）
                                                │    ├─ 难度栏（常驻，随选中曲刷新，含锁定态）
                                                │    └─ 开始 / 设置按钮
                                                └─ PopupLayer  弹出层栈
                                                     ├─ 设置弹层（键位 / 偏移 / 音量…）
                                                     ├─ 重绑"请按键"提示
                                                     └─ 确认框等
                                                          ▲
                                                 MenuNavigator（弹层栈 + ESC 分发）
```

（图中 `SettingsService`、设置弹层、重绑提示均为**后续版本**，v1 不实现。）

**1. 数据/服务层**（全部挂 ProjectContext）

- 选歌页读 `TrackCollection` 填列表；点"开始"写 `TrackSelectionService` 再 LoadScene
- 设置页的延迟/音量等存进新增的 `SettingsService`，局内装配时从它读（后续版本）

**2. 导航层**（薄）

- 选歌页是唯一常驻页面；设置与各类提示都是弹出层（弹层栈），ESC 统一分发：有弹层先关弹层
- 细节：重绑捕获期间 ESC 被重绑流程吃掉，导航器需要"捕获中"状态并忽略（否则 ESC 既取消重绑又触发返回）——后续版本做重绑时处理

**3. 视图层**（UGUI，MVP 拆分）

- 每个页面/弹层 = **View**（prefab 上的 MonoBehaviour，被动）+ **Presenter**（纯 C#，逻辑与状态），约定见 3.3
- 原则：**面板之间零引用**。横向通信用导航器和服务层，数据从服务层拿
- 歌曲 cell 是纯显示对象，点击只发事件；轮盘常驻 n+1 个实例循环复用（占位槽 + 1 缓冲，不销毁重建，见"4.5 轮盘机制"）

### 3.3 MVP 拆分约定（强制）

- **View**（prefab 上的 MonoBehaviour）：只持序列化视图引用；对外暴露"用户输入事件"与 `SetXxx` 更新方法；**零逻辑、零服务访问**
- **Presenter**（纯 C#）：持服务与浏览状态（光标 index / 当前难度）；订阅 View 事件、调 View 更新方法；有分支的逻辑（难度回退等）配 EditMode 单测
- **装配**：View 由 `MenuSceneInstaller` `[SerializeField]` 手拖，Presenter 在 `InstallBindings` 里 `WithArguments(view)` 绑定
- **生命周期**：v1 面板（选歌页、结算）都是场景级 → Presenter `AsSingle` + `IInitializable`；弹层（设置）的生命周期策略（预实例化 vs Factory）等做设置功能时再定

### 3.4 技术立场

- **菜单面板采用 MVP**（2026-10-09 拍板）。收益：视图/逻辑分离由结构保证、Presenter 可单测；代价：类数翻倍（接受）
- **歌曲 cell 不上 Zenject Factory**——cell 没有任何依赖需要注入，池化 + 手工绑数据更简单
- **UGUI**（理由见"一"）

---

## 四、实现蓝图（v1）

> 类名与接口为示意，实现时可微调，但"View 哑巴 / Presenter 大脑"的分工不变。

### 4.1 GameObject 与 Prefab 布局

```
MenuScene
├─ SceneContext              （Zenject 场景入口）
├─ MenuSceneInstaller        （手拖 View 引用 + 绑 Presenter）
└─ Canvas
   ├─ SongSelectPanel        ← SongSelectView 挂这
   │  ├─ TrackList           （轮盘容器：cell 池 + 布局驱动）
   │  │  ├─ SongCell ×7      ← SongCell.prefab（池化，仅可见窗口 = 选中 ±3，每格挂 SongCellView）
   │  │  └─ PositionIndicator（位置指示条，Scrollbar 非交互）
   │  ├─ DetailPanel         （Cover + DetailText 单文本：曲名 / 曲师 / 谱师 / BPM）
   │  ├─ DifficultyBar       （4 个难度按钮）
   │  ├─ StartButton
   │  └─ SettingsButton      （v1 占位）
   └─ PopupLayer             （v1 为空，以后放设置弹层等）

TrackPlayingScene（结算部分追加）
└─ Canvas
   └─ ResultsPanel           ← ResultsPanel.prefab（挂 ResultPanelView）
```

### 4.2 类清单

**MenuScene 侧（v1）**

| 类 | 类型 | 职责 | 对外接口（示意） |
| --- | --- | --- | --- |
| `SongSelectView` | MonoBehaviour | 持全部序列化引用（轮盘 / 详情区 / 难度按钮 / 开始按钮）；管 cell 池与轮盘布局（光标 index → 位置 / 缩放 / 透明度映射） | 事件：`TrackClicked`（点格切到该曲）/ `StepRequested(±1)`（滚轮 / ↑↓）/ `DifficultyClicked` / `DifficultyStepRequested(±1)`（←→）/ `StartClicked`；方法：`SetTracks(...)` / `SetCursor(index)` / `SetDetail(...)` / `SetDifficultyStates(...)` / `SetStartInteractable(...)` |
| `SongCellView` | MonoBehaviour | 单个 cell 显示（封面缩略图 / 曲名）与深度视觉（缩放 / 透明度 / 高亮态） | `Bind(track)` / `SetLayout(...)` / 事件 `Clicked` |
| `DifficultyButtonView` | MonoBehaviour | 单个难度按钮（场景对象，无 prefab）：点击上抛 / 状态收敛 / 选中置顶 | `SetState(hasChart, constant, selected)`；事件 `Clicked` |
| `SongSelectPresenter` | 纯 C# | 页面大脑：初始化拉数据；维护"光标 index / 当前难度"状态；难度回退与锁定规则；点开始 → 写 `TrackSelectionService` → 加载打歌场景 | `IInitializable`，内部订阅 View 事件 |
| `MenuNavigator` | 纯 C# | 导航缝：v1 只管"进入打歌"（弹层栈随设置功能再长出） | `EnterGame()` |
| `MenuSceneInstaller` | MonoInstaller | `[SerializeField]` 手拖各 View，`WithArguments(view)` 绑 Presenter | — |

**TrackPlayingScene 结算侧**

| 类 | 类型 | 职责 |
| --- | --- | --- |
| `ResultPanelView` | MonoBehaviour | 显示分数 / 连击 / 判定统计；按钮发事件 `RetryClicked` / `BackClicked` |
| `ResultPresenter` | 纯 C# | 订阅 Wwise 音乐结束回调；读 `ScoringSystem` 组装结果 → 命令 View 显示；重试=重载场景、返回=载 MenuScene；负责 Playing → 结算 的阶段切换与清场 |

### 4.3 信息传递

**开局（数据下推）**

1. Zenject 建容器 → new `SongSelectPresenter`，注入 `TrackCollection` / `TrackSelectionService`，并塞入手拖的 View 引用（依赖注入本身就是第一段传递）
2. Presenter `Initialize`：读 `TrackCollection` → `view.SetTracks(tracks)`（列表一次性下推，之后只传 index）
3. View 池化出可见窗口 cell（选中 ±3），逐个 `Bind(track)` 并按光标距离映射摆位
4. Presenter 定初始光标（按引用反查 `SelectedTrack` 索引，见决策 11）→ `SetDetail(track, 选中难度谱面)` + 计算四个难度状态（有谱 / 无谱，含定数）→ `SetDifficultyStates(...)` + `SetStartInteractable(...)`

**用户点击（事件上抛、循环闭合）**

1. 点歌曲 → `SongCellView.Clicked` → `SongSelectView.TrackClicked(track)` 上抛（点谁切到谁）
2. Presenter 更新状态（选中切到该曲）→ 命令 View：`SetCursor(index)` + 刷新详情区与难度栏
3. 点难度 → `DifficultyClicked` → Presenter 走规则（锁定不可选等）→ 更新 `_selectedDifficulty` → View 刷新
4. 点开始 → `StartClicked` → 写 `TrackSelectionService` → `MenuNavigator.EnterGame()` 加载打歌场景

**结算**

Wwise 音乐结束回调 → `ResultPresenter` 收 → 读 `ScoringSystem` → `SetResult` + `Show()`；重试 / 返回 → View 上抛 → Presenter 重载场景或载 MenuScene。

规律一句话：**服务数据只流向 Presenter，Presenter 命令 View 显示；用户输入从 View 上抛回 Presenter**。View 不碰服务；Presenter 不碰 UGUI 细节（只说"该难度被锁"，不说"把锁图标 SetActive"）。

### 4.4 文件位置

`Assets/Features/UI/Scripts/`（View 与 Presenter 同目录）、`Assets/Features/UI/Prefabs/`、`Assets/Features/UI/Scenes/MenuScene.unity`。

### 4.5 轮盘机制（切歌实现细节，2026-10-11 定）

**单一光标浮点（同步性由结构保证）**：View 持有唯一动画状态——连续浮点 `cursor`（单位 = 曲目索引空间）+ 整数目标 `cursorTarget`（Presenter 给的）。切歌 = 只改目标值；View 每帧把 cursor 向目标插值（步骤 4 起用 DOTween tween 这一个浮点）。**所有格子动画 = 同一光标每帧被采样的结果**，不是 N 个独立动画靠对齐——快速连切只是改一个数，天然平滑。

**每帧映射**：`slot = 该实例绑定的曲目索引 − cursor` → 查槽位表 → 写 `anchoredPosition` / `localScale` / `CanvasGroup.alpha`（+ 中心格高亮）。`SongCellView` 零动画逻辑（无 Update、无 tween），只有被动口 `Bind(track)` / `SetLayout(...)`。

**池 = 槽位表 n + 1**：默认 7 槽（选中 ±3）；槽位表可配且支持非对称（如 3 上 4 下 = 8 槽）；池大小 = 槽位数 + 1 运行时推导。过渡中点两端各有一个"透明在位"格（峰值 n+1）——多的一格缓冲让回收/换绑只需发生在"完全透明"的任意时刻。

**换绑不变量**：①槽位表最外槽 + 0.5 处 alpha 必须为 0（= 回收阈值）；②换绑（实例改绑另一曲目）只发生在该实例完全透明时——**可见格永不换内容**。

**数据流**：Presenter 初始化一次性 `SetTracks(list)`；此后切歌只发 `SetCursor(k)`（+ `SetDetail` / `SetDifficultyStates`）。View 自持列表按 index 为进场格换绑；边界（0 / N−1）时无歌可绑的实例停用（无环绕）。指示条随 cursor 更新（纯显示）。

**槽位表**：每槽 `{ 偏移Y, 缩放, 透明度 }`（View 序列化配置，条数任意）；"选中在哪"由 slot 0 的偏移决定；步骤 1 手摆的深度参数即槽位表初值。

**4.5 实现修订（2026-10-11，轮盘已落地）**：
> ① 槽位表 = `{X, Y, 缩放, Alpha}`；默认 **9 槽**（−4..+4）：索引 0 = 最上、正中条 = 光标槽、**最外两条 = 出场位（Alpha 恒 0 = 回收线）**；池 = 条数 + 1。出场“移动中淡出”由槽表表达（外圈的位置 / 缩放姿态 + Alpha 0）。
> ② 采样：相邻槽插值；贴近中心的两段（|rel| ≤ 1）的 **X / 缩放走方向相关缓动**（靠近中心、离开中心各一条指数曲线，实现“快进快出”）；Y / Alpha 线性。
> ③ 格子显示层：`Bind(曲目)` / `SetLayout(X, Y, Scale, Alpha)` / `SetSelected(强度 0..1)`（选中表现 = 文字色与底条色按强度插值）。
> ④ `SongSelectView` 先行收窄为**纯轮盘**（详情区 / 难度栏 / 开始按钮随后续步骤加回）；联调用 `Temp/TempWheelTest`（假数据）已于 2026-10-12 删除。
> ⑤ 输入接线（2026-10-12，步骤 4a）：原始输入捕获在 View（滚轮 `IScrollHandler` / ↑↓ `Update` 读键），统一 0.08s 节流后上抛 `StepRequested(±1)`（上滚 / ↑ = −1）；"能否切、切到哪"（首尾钳制）与光标状态在 Presenter；点格 = `TrackClicked` 切到该曲居中。
> ⑥ 恢复上次选曲（2026-10-12，步骤 3）：Presenter 按**引用反查** `SelectedTrack` 在列表中的索引（找不到回 0），不向 `TrackSelectionService` 加索引字段；难度状态恢复自 `SelectedChart.ChartDifficulty`。详见决策 11。
> ⑦ 难度栏（2026-10-12，步骤 4b）：4 按钮 = 独立 `DifficultyButtonView`（不用 Button 组件；点击上抛；三态配色 / 选中缩放 = 指数收敛；数字无谱留空；选中时 `SetAsLastSibling` 置顶——前提：DifficultyBar 的 LayoutGroup 已禁用、位置手摆，调层级不影响布局）；←→ 步进切难度（无循环、缺谱跳过）；点格 / 滚轮 / ↑↓ 切歌均触发难度栏刷新。
> ⑧ 详情区 + 开始流程（2026-10-12，步骤 4c / 6）：详情区 = Cover(Image) + 单 TMP 文本，文案 `{曲名} Composer：{曲师} Charter：{谱师} BPM：{bpm}`（Charter 取当前选中难度谱面；缺字段 / 无谱 / BPM≤0 占位「—」；不含定数——定数只在难度栏），随切歌 / 切难度与难度栏同批下推。开始 = StartButton 保留 Unity Button（Awake 接 onClick 上抛；禁用态 `interactable`）；Presenter 写 TrackSelectionService → MenuNavigator.EnterGame → `SceneManager.LoadScene("TrackPlayingScene")`（无过渡）；TrackAssetInstaller 默认选曲移除 + 局内非空校验（问题 3 解决）。
> ⑨ 菜单键盘输入迁移（2026-10-15，输入模块重写连带，见决策 19）：键盘方向键从 View 的旧 Input 轮询迁到 `SongSelectPresenter`（ITickable 轮询 `InputService.Menu*`），0.08s 节流随迁（键盘与滚轮仍共享同一节流）；View 仅剩 UGUI 指针事件（点格 / 滚轮 / 按钮）；`DifficultyStepRequested` 事件随键盘 raiser 一并删除。
> 上文“7 槽 / 最外槽 + 0.5 处 alpha 为 0”等描述以本修订为准。

---

## 五、实施计划（逐步推进）

> **执行协议（每一步都适用）**
> 1. 只做当前步骤这一层，**不要顺手做下一步**；
> 2. 做完停下，向用户讲清：①本步产出了什么 ②它对应 MVP 的哪一层 ③数据往哪个方向流 ④在哪里验证；
> 3. 等用户确认后再进入下一步。禁止一次性写完整套。

**步骤 0：前置打通（v1 阻塞）**
- 目标：建空 MenuScene；两场景进 Build Settings；定案启动场景与 WwiseGlobal 归属（**需与用户确认方案**，见"九"）
- 产出：空场景能启动、能与 TrackPlayingScene 互相 LoadScene
- 验证：Play → 菜单 ↔ 打歌 手动互跳成功

**步骤 1：静态 UI（纯视觉）**
- 目标：SongSelectPanel 的 UGUI 布局手摆（轮盘版）——7 个占位格按深度排布（选中居中 + 缩放/透明度衰减）/ 位置指示条 / 详情区 / 难度栏 / 开始、设置按钮；零脚本
- 产出：MenuScene 里可见的完整版面（即轮盘效果原型图）
- 验证：编辑器内布局、缩放正常；深度层次视觉成立

**步骤 1 操作清单（2026-10-11 定稿；Unity 侧由用户手动执行）**

> **2026-10-11 修订**：版式（位置/尺寸/美术）改由用户在场景中自行迭代定稿——以下具体版式数值不再作为验收依据，Agent 不再提供版式数值建议；收尾核对以 `MenuScene.unity` 实际为准，定稿后按实际结构同步本节与 4.1。

- Canvas：`UI > Canvas` → 命名 `Canvas`；CanvasScaler = Scale With Screen Size / 1920×1080 / Match 0.5；自动生成的 EventSystem 保留
- 层级与命名（严格照此，步骤 2 序列化引用对名）：
  ```
  Canvas
  ├─ SongSelectPanel          全屏拉伸（步骤 2 挂 SongSelectView）
  │  ├─ Background            全屏 Image 深色底（如 #14141E）
  │  ├─ TrackList             轮盘容器：左拉伸 PosX 40 / Width 680 / Top 40 / Bottom 40；Image 深色半透明（raycastTarget ✓）
  │  │  ├─ SongCell_1 … SongCell_7
  │  │  └─ PositionIndicator  位置指示条（Scrollbar）
  │  ├─ DetailPanel           anchorMin(0.4,0) anchorMax(1,1)；Left 60 / Right 80 / Top 60 / Bottom 60
  │  ├─ DifficultyBar         anchorMin(0.4,0) anchorMax(1,0) pivot(0.5,0)；Left 60 / Right 80 / PosY 240 / Height 110
  │  ├─ StartButton           anchorMin(0.4,0) pivot(0,0)；PosX 60 / PosY 60 / 460×110
  │  └─ SettingsButton        anchor(1,0) pivot(1,0)；PosX -80 / PosY 60 / 240×110
  └─ PopupLayer               空 RectTransform，全屏拉伸
  ```
- SongCell 占位（1 个复制成 7）：656×132，锚 middle-center，root 加 CanvasGroup；子物体：`Highlight` 左侧竖条 Image 宽 8（仅中心格 SetActive 开）、`Cover` 108×108（锚 middle-left、靠左 12）、`TitleText`（锚上拉伸 Left 132 / Right 24 / Top 18 / 高 36，字号 30）、`ArtistText`（Left 132 / Right 24 / Top 62 / 高 28，字号 22 灰）
- 深度参数（先拍此值；验收后作为步骤 2 槽位表初值）：

  | SongCell | 相对选中 | PosY | Scale | CanvasGroup Alpha | Highlight |
  | --- | --- | --- | --- | --- | --- |
  | 1 | −3 | +400 | 0.70 | 0.10 | 关 |
  | 2 | −2 | +300 | 0.78 | 0.28 | 关 |
  | 3 | −1 | +170 | 0.88 | 0.55 | 关 |
  | 4 | 0 | 0 | 1.00 | 1.0 | 开 |
  | 5 | +1 | −170 | 0.88 | 0.55 | 关 |
  | 6 | +2 | −300 | 0.78 | 0.28 | 关 |
  | 7 | +3 | −400 | 0.70 | 0.10 | 关 |

- PositionIndicator：`UI > Scrollbar`；Direction = Top To Bottom；Interactable 取消勾选；Handle Size ≈ 0.15；Value 手填 0.5；贴 TrackList 右缘（右拉伸，Width ≈ 12，上下留 20）
- DetailPanel 子物体：`Cover` 480×480（锚 top-left，Pos 0,0）；`TitleText` Pos(510,0) 500×90 字号 56；`ArtistText` Pos(510,−100) 字号 30；`ConstantText` Pos(510,−160) 字号 36（定数）；`CharterText` Pos(510,−210) 字号 26（谱师）
- DifficultyBar：HorizontalLayoutGroup（Spacing 16 / Control Child Size W+H ✓ / Force Expand Width ✓）+ 4 个 Button，命名 `DifficultyButton_EZ / _ST / _HD / _EX`；按钮自带文字改名 `Label`，另加小 TMP `Const`（定数占位如 "–"；若定数只放详情区可删）
- StartButton / SettingsButton：`Button - TextMeshPro`，文字 "START" / "SETTINGS"（尺寸见层级表；设置按钮 v1 占位）
- 占位文字一律英文/数字：TMP 默认字体（LiberationSans）无中文字形，中文显示为方块——中文字体在步骤 3 接真实数据前处理
- 禁区：不加任何脚本、不建 prefab、不建 SceneContext / MenuSceneInstaller（步骤 2/3）
- 附加验证：1920×1080 与 1280×720 下版面不崩；Play 无交互属预期（零脚本），F9 互跳照旧；Ctrl+S 保存

**步骤 2：View 层（哑巴）——已完成（2026-10-11，轮盘经 TempWheelTest 联调通过）**
- 目标：`SongSelectView` 序列化引用齐全 + `SongCellView`（含 `SongCell.prefab`，cell 池在 View 内）——只有事件口与 Set 口，无逻辑
- 产出：View 脚本 + cell prefab
- 验证：可编译；不接数据时状态符合设计
- 讲解点：View 为什么不能碰服务

**步骤 3：Presenter 第一版（数据下推 + 恢复上次选曲）——已完成（2026-10-12）**
- 实施：`SongSelectPresenter` + `MenuSceneInstaller` 装配（`WithArguments(view)`）；`Initialize` 全量下推 `TrackCollection` 曲目 + 按引用反查 `SelectedTrack` 恢复光标（找不到回 0，见决策 11）+ 从 `SelectedChart` 恢复难度状态（`_selectedDifficulty`，难度栏未加回、暂存）
- 验证：Play → 轮盘显示真实曲目列表（用户实机验证通过）

**步骤 4a：切歌输入闭环——已完成（2026-10-12）**
- 实施：View 捕获原始输入（点格 `IPointerClickHandler` 已有；滚轮 `IScrollHandler`；↑↓ `Update` 读键），0.08s 节流后上抛 `StepRequested(±1)`（上滚 / ↑ = −1）；Presenter 持 `_cursorIndex` 做 ±1 与首尾钳制（无环绕）→ `SetCursor`；点格 = `TrackClicked` 切到该曲。DOTween 未引入（手动"单浮点指数收敛"已满足平滑 + 吸附，见决策 11）
- 验证：用户实机验证通过（点格 / 滚轮 / ↑↓ / 边界钳制 / 节流）
- 遗留：详情区刷新未做（子视图未加回）

**步骤 4b：难度栏与难度选择——已完成（2026-10-12，实机验证通过）**
- 拍板（决策 12）：回退规则 = 就近向低；锁定态 v1 不做（只"有谱 / 无谱"两态）；详情区后置为步骤 4c；难度按钮不用 Unity Button 组件（交互按 SongCell 模式：IPointerClickHandler + 脚本自做过渡）
- 实现：`DifficultyState` 契约；`DifficultyButtonView`×4（点击上抛 / 三态配色与选中缩放指数收敛 / 数字无谱留空 / 选中 SetAsLastSibling 置顶）；`SongSelectView`（DifficultyClicked、←→ DifficultyStepRequested，0.08s 节流）；Presenter（全量下推 / 有谱才采纳 / 就近向低回退 / ←→ 无循环缺谱跳过）
- 场景现状（用户实装）：DifficultyBar 的 HorizontalLayoutGroup 已禁用（按钮位置手摆，调层级置顶的前提）；难度按钮无 Button 组件；按钮子物体 Label = 等级文本、Difficulty = 数字文本
- 产出：可浏览、可感知选中、可选择难度（点击 + ←→）
- 验证：切歌 → 难度栏联动（定数数字 / 选中 / 缺谱灰态）；点难度与 ←→ → 选中迁移、跨曲保留、缺谱跳过、两端钳制；选中按钮置顶
- 下一步（新对话，二选一）：步骤 5（规则收口 + 首条 EditMode 单测，顺延）或步骤 7（结算侧，前置 = Wwise 音乐结束回调）

**步骤 4c：详情区下推与刷新——已完成（2026-10-12，实机验证通过）**
- 定案（动工前确认）：详情区不含定数（定数只在难度栏）；文案 = `{曲名} Composer：{曲师} Charter：{谱师} BPM：{bpm}`（单 TMP 文本；Charter 取当前选中难度谱面）；缺字段 / 无谱 / BPM≤0 占位「—」；结构保持用户简化后的 Cover + 单文本（未拆回多对象）
- 实现：View 加 `_coverImage` / `_detailText` 引用与 `SetDetail(track, chart)`（格式与占位拼装在 View 层）；Presenter 在 `RefreshDifficultyStates` 末尾与难度栏同批下推（切歌 / 切难度 / 初始化统一覆盖）
- 验证：切歌 / 切难度 → 封面与文本随动（用户实机验证通过）

**步骤 5：规则收口——已完成（2026-10-13；单测按用户决定不做）**
- 复查结论：难度回退 / ←→ 缺谱跳过 / 点难度只采纳有谱 / 开始校验等规则在 4b / 4c 落地时已在 Presenter；本步收的是残余散落点：① 难度数量常量单源化（删 `SongSelectView` 副本，View 改用按钮数组长度；`SongSelectPresenter.DifficultyCount` 为唯一定义）② 光标钳制单源化（删 View `SetCursor` / `SetTracks` 的防御 Clamp——决策 11"钳制在 Presenter"，空态 guard 保留）③ `OnDifficultyClicked` 注释纠偏（旧注释称"无谱按钮在 View 侧已不可点"与实现不符；View 不拦、规则唯一在 Presenter）
- 定案（2026-10-13 用户决定）：EditMode 单测本步不做；"为什么 Presenter 能脱离 Unity 测试"原理已讲解（Presenter 非 MonoBehaviour 可直接 new / 输入输出皆普通方法、可替换替身并断言指令流 / ITickable 帧可手动推）
- 验证：改动行为中性（删的是重复防御与失真注释）；选歌 / 切歌 / 切难度 / 开始回归随日常游玩覆盖

**步骤 6：开始流程——已完成（2026-10-12，实机验证通过）**
- 定案（动工前确认）：StartButton **保留 Unity Button 组件**（用户确认，与"仿 SongCell 不用 Button"的约定不同——例外；Awake 里接 onClick 上抛 `StartClicked`，不用 Inspector 挂回调；禁用态走 `Button.interactable`）；点击后直接 `SceneManager.LoadScene`（无过渡，过渡动效后续版本）
- 实现：View（`_startButton` 引用 + `StartClicked` 事件 + `SetStartInteractable`）；Presenter（`OnStartClicked` 写 `SelectedTrack / SelectedChart` → `_navigator.EnterGame()`；整曲无谱兜底返回）；`MenuNavigator.EnterGame` = `LoadScene("TrackPlayingScene")`（场景名常量硬编码）
- 前置清理：`TrackAssetInstaller` 默认选曲移除（现有代码问题 3 解决）；`TrackPlayingInstaller`（SelectedChart）与 `TrackController.Start`（SelectedTrack）加非空校验报错——直接 Play 打歌场景时给出明确提示
- 验证：选曲（含难度）→ 开始 → 正确曲目 / 难度开打（用户实机验证通过）；打歌场景只有从菜单进才有声音（决策 9）
- 遗留：TempSceneJump——已核对（2026-10-13）：MenuScene 无 F9 残留；TrackPlayingScene 的 F10 调试实例保留（中途退出调试用）

**步骤 7：结算侧——已完成（2026-10-13，实机验证通过）**
- 定案（动工前确认）：① 结算信息 = 单个 text 承载 TrackSelectionService + ScoringSystem 两来源（格式在 `ResultPresenter.BuildResultText()` 用户自写）② 重试 / 返回按钮用 Unity Button（纯点击交互；接法同 StartButton）③ 曲终残留 Note 不清场（面板盖住；TrackTimeMs 曲终归 0 会让残留 Note 回跳，面板全屏不透明则不可见）④ 不新增 Phase 枚举（ResultPresenter 独占"曲终 → 出面板"路径）
- 实现：`ResultPanelView`（文本 + 重试/返回两 Button，Awake 接 onClick 上抛；用户命名 Continue / `ContinueClicked`）+ `ResultPresenter`（IInitializable 订阅 `TrackEnded` → 组装文本 → 出面板；重试 = 重载打歌场景、返回 = `LoadScene("MenuScene")`；开局隐藏面板）；删除 `TempTrackEndReturn`
- 验证：曲终自动出结算；重试重开同一曲；返回回菜单（用户实机通过）

**打歌 HUD（分数 / Combo / 进度条）——已完成（2026-10-13，代码 + Unity 侧；用户已进入下一步）**
- 定案：进度数据源 = 音乐段落时长（`iPreEntryDuration + iActiveDuration` → `TrackController.TrackDurationMs`；弃用编辑器侧不可靠的 `AkUtilities.GetEventDurations`，默认实现恒返回 -1）；Combo 低于阈值隐藏（View 序列化，默认 2）；曲终进度冻结在满（只前进不后退）；不做暂停 / 动画 / 判定飘字
- 实现：`GameHudView`（分数 TMP / Combo TMP / 进度 Image Filled）+ `GameHudPresenter`（**ITickable** 每帧读、变化才下推）；`TrackPlayingInstaller` 装配
- 验证：分数随判定增长；连击累加 / 断连隐藏；进度推进至满、曲终不跳回

**暂停功能（打歌中，无 UI 版）——已完成（2026-10-13，实机验证通过）**
- 定案（用户确认）：本步只做暂停功能内核 + 临时测试按键，**不做任何 UI**；触发 = 临时测试按键（**旧输入系统 P 键**，与正式输入映射解耦）；阶段结构走**最小实现**（不新增 Phase 枚举）；音乐暂停 = Wwise Pause / Resume 事件（位置保留、原位续播，`TrackController._isPaused` 预留字段转入使用）
- 实现：`TrackController.Pause() / Resume() / IsPaused`（守卫 = 已开始 / 未暂停 / 非停止）+ `Update` 暂停早退（冻结 TrackTimeMs 时间源 → Note 视觉 / 轨道动画 / 判定 / HUD 随之全部停摆；暂停期间也不采集轨道输入）；临时测试键 `PauseTestKey`（纯 C# ITickable，P 键切换；`TrackPlayingInstaller` 一行绑定）——面板落地后删除本文件与绑定行
- 首轮实机修复（2026-10-13）：pause / resume / stop 原误用 `ExecuteActionOnEvent`（首参 = **事件 ID**；传 playingID → 报 `Event ID not found` 且动作静默失效——音乐不停只冻结了判定，恢复时按真实播放位置大幅跳跃）；改用 `ExecuteActionOnPlayingID(动作, playingID, 0, Linear)`。存量 `StopAndUnloadTrack` 的 Stop 同错一并修复（此前仅靠 bank 卸载兜底）
- 验证（实机通过）：按 P 音乐停 / 判定停 / 画面冻 / HUD 冻；再按 P 原位续播、判定恢复；加载中与曲终后按 P 无效果；**存量 Wwise 报错一并消失**（StopAndUnloadTrack 同源修复的连带效果）

**暂停面板 UI——已完成（2026-10-13，实机验证通过）**
- 动工前确认（2026-10-13 用户拍板）：① 按钮集 = 继续 / 重试 / 返回选歌 ② 恢复 = 3-2-1 倒数后续播（新增 Resuming 阶段；倒数期间音乐保持暂停、ESC 忽略）③ 正式触发 = ESC 键（`InputSystem.Instance.EscDown`，Back 动作已绑 `<Keyboard>/escape`；落地后删 PauseTestKey 与装配行）④ 阶段结构显式化 = `TrackController.Phase`（TrackPhase 枚举：Loading / Playing / Paused / Resuming / Result）；`IsPaused` / 公开 `Resume()` 由 `Phase` / `RequestResume()` 取代 ⑤ 面板归属（既定）：prefab + 场景内 presenter 控制显隐，同 ResultPanel 模式；版式用户自定
- 实施分层（增量）：L1 TrackController 状态机 + 倒数（实机通过）；L2 PausePanelView（用户手搭面板）；L3 PausePanelPresenter + ESC 输入路由（PauseInputRouter）+ 装配（PauseTestKey 已删）——全部完成
- L1 实机验证结论（2026-10-13）：P 暂停 / 二次 P 走 3-2-1 倒数原位续播 / 倒数中与曲终后按 P 无效果，全部通过
- 附带行为变化：曲终 Result 阶段时间源冻结在曲末（原 TrackTimeMs 归 0 致残留 Note 回跳的现象消除；结算面板仍盖住）
- 首轮实机修复（2026-10-13，暂停 → 重试卡 Loading）：新场景 `PlayTrack` 永久卡在 `LoadAsync` 的 await、Console 无任何日志。根因 = 与集成层语义错配：`SoundBank.Unload()` 仅 DecRef 进延迟卸载队列，真正卸载（引擎 UnloadBank + 管理器摘 handle）在下一帧 `LateUpdate` 的 `DoUnloadBanks()`；场景连打时重载的 `LoadAsync` 命中 `AkBankManager`「handle 已存在」分支——`IncRef` 后静默返回、不回回调，await 永久挂起。修复：① `PlayTrack` 改**同步 `SoundBank.Load()`**（对「已加载 / 待卸队列中」均有确定的复用语义，不依赖回调；代价 = 首次进歌主线程阻塞一瞬）② `StopAndUnloadTrack` 卸载后立即 `AkBankManager.DoUnloadBanks()`，卸载当场落定 ③ teardown 暂停 / 倒数态先 Resume 再 Stop（保证引擎卸载可进行）。**禁止改回 LoadAsync + await 模式。**
- 验证（2026-10-13 实机通过）：ESC 出面板；继续 → 3-2-1 倒数续播；重试重开 / 返回选歌正常（修复后不再卡 Loading）；暂停与倒数中判定、轨道输入维持冻结

**步骤 8：闭环验证——已完成（2026-10-13，实机验证通过）**
- 目标：Menu ↔ Track 循环游玩整体验证
- 验证：不重启连续游玩 ≥3 轮，选曲 / 结算 / 重试均正确（2026-10-13 用户实机通过）

**分数存档系统接入——下一步（2026-10-13 规划；动工前确认）**
- 目标：打通"打歌出分 → 存档 → 读取回显"链路；为难度解锁（决策 3，数据来源待定）与最佳成绩展示铺数据
- 现有铺垫（已核对 2026-10-13）：`SaveManager`（Common，`RuntimeInitializeOnLoadMethod` 自建单例；按"类型 + key"隔离存 JSON、脏标记、退出时强制保存，口子 = `Get<T>` / `SetDirty<T>` / `Save<T>` / `SaveAllDirty`）；`ChartInfo.ScoreSave`（每谱面一个"存档名"字段，已预留待用）
- 动工前待确认：① 存档内容粒度（最佳分数 / 评级 / 最大连击 / 判定计数？是否记游玩次数、是否区分"最佳 / 最近"）② 存档 key 口径（用 `ChartInfo.ScoreSave` 命名 or 曲目 + 难度派生）③ 写入时机与规则（结算出分处写入——ResultPresenter 侧；重打"取优"还是"覆盖最近"）④ 读取展示点（首处做哪：选歌详情区最佳成绩 / 结算面板 "new best" 标记 / 解锁判断暂缓）⑤ 数据类型归属（新增可序列化的 `ScoreRecord` 类，随 SaveManager 类型隔离走）
- 前置：结算出分链路已就绪（`ResultPresenter` + `ScoringSystem`，实机通过）；不动 v1 已闭环的 Menu ↔ Track 流程
- 验证（预定）：曲终写入 → 重进（含重启进程）读取回显正确；同曲重打按定案规则更新；选歌 / 结算回归不受影响

---

## 六、执行约束（Agent 必守）

1. **逐步实现**：严格遵守"五"的执行协议。每步只做一层、做完讲解、确认后继续——用户要靠这个过程理解 MVP。
2. **MVP 纪律**：View 零逻辑、零服务访问；Presenter 纯 C#（不挂 GameObject）；面板之间零互引（横向通信走导航器 / 服务层）。
3. **DI 约定**（沿用项目现状）：场景对象 View 用 `[SerializeField]` 手拖；Presenter 在 `InstallBindings` 里 new 后 `BindInstance`（`WithArguments(view)`）；跨场景服务挂 ProjectContext。
4. **不做延后项**：设置弹层、试听、解锁机制都不属于 v1，不要顺手实现。
5. **待定岔路先问**：命中"九、待定事项"的内容，先与用户确认，不要自行定案。
6. **代码风格**：沿用项目现状（公开方法带 XML 注释）；大范围重命名不自己做，告诉用户用 Rider 完成。
7. **Unity 侧由用户手动操作（2026-10-11，本任务实施期间的约定）**：Agent 不调用 Unity 自动化接口；职责 = ①给出可照做的操作清单（层级 / 命名 / 组件 / 参数）②用文件读取核对场景与脚本产物 ③讲解与验收要点。

---

## 七、现有代码问题（按对 v1 的影响标注）

1. **（设置功能动工前，不阻塞 v1）`InputSystem.RebindTrackAction` 条件反了（InputSystem.cs:77）**

   ```csharp
   if (index is >= -3 and <= 3)   // 合法轨道号直接走 cancel 返回！
   ```

   合法索引（-3~3）会直接取消返回，非法索引反而往下走 `lanesAction[index]` 抛异常。应为 `< -3 or > 3`。即**重绑功能目前整个是坏的**，设置页动工前先修，并在 Unity 里实际跑一遍验证。**——已解决（2026-10-15，输入模块重写）：** 重绑在 `InputService.StartRebind` 中整体重写（条件、Disable/Enable 时序、落盘时机）；API 更名对齐新输入层（见决策 19）；**交互实机验证延后到设置弹层落地**（本轮未加临时测试键）。

2. **（清理项）`TrackSelectionServiceInstaller.cs` 是死代码**——没有任何 prefab/scene 引用它。实际绑定发生在 `TrackAssetInstaller`（ProjectContext 的 SO installer）里。

3. **（v1 相关）`TrackAssetInstaller` 硬编码默认选择——已解决（2026-10-12，步骤 6）**：默认值已移除，选曲由菜单（`SongSelectPresenter.OnStartClicked`）写入；`TrackPlayingInstaller` 与 `TrackController.Start` 加非空校验报错。（原文："默认选 0 号曲、0 号谱"的测试残留，菜单接管选曲后应去掉默认值。）

4. **（v1 阻塞）启动流程未定义**：菜单作为入口后要定——启动场景是谁、Build Settings 清单、Wwise 初始化（WwiseGlobal）放哪个场景、是否 DontDestroyOnLoad。**——已定案（2026-10-10，见决策 9）。**

5. **（设置功能动工前，不阻塞 v1）重绑无冲突检测**：同一个键绑到两条轨道不报错。音游一般要提示，做不做是交互决策。**——已定案并完成（2026-10-16，决策 20）：不做检测——一键多轨为规则内合法，不构成冲突。**

---

## 八、决策记录（2026-10-09 拍板）

1. **设置页形态：弹层覆盖选歌页。** 导航器 = 选歌页 + 弹层栈，不需要页栈。
2. **选歌布局：垂直列表 + 固定详情区。** 列表项为封面缩略图 + 曲名/曲师；详情区显示大封面、定数、谱师等。（列表形态已于 2026-10-10 改为切歌轮盘，见决策 10 / 4.5）
3. **难度选择：难度栏常驻选歌页，独立于曲目选择。** 随选中曲目实时刷新对应定数显示；曲目没有的难度留空，未解锁的显示为上锁。
4. **曲目试听：预留接口，v1 不做。** 框架保留"选中曲目变化"的事件口子，音频接入时补；`MusicExcerpt` 字段留用。
5. **延迟设置：v1 做单偏移 + 数字调节。** 后续在设置弹层内加"节拍辅助校准"子界面：播放节拍参考，玩家手动调数字对齐（仍是同一个单偏移值）。
6. **持久化：键位维持独立存储。** 键位覆盖继续由 InputSystem 自管 `customBindings.json`；其余设置（流速、偏移等）由 `PlayerConfigStore` 持有（2026-10-15 修订：原拟 `SettingsService` + `SaveManager`，实落为 Persistence 模块 `PlayerConfigStore`，见《CQProject-存档系统设计》"九"；音量等待后续字段）。接受存储分散的代价。
7. **结算界面：置于 TrackPlayingScene 内，作为局内阶段**（不为结算开新场景）。触发 = Wwise 音乐结束回调；面板为 prefab，场景内 presenter 读 `ScoringSystem` 直接出分；按钮仅"重试"（重载场景）/"返回选歌"（LoadScene MenuScene）。场景流 = MenuScene ↔ TrackPlayingScene 循环。
8. **菜单 UI 采用 MVP：View 被动 + Presenter 纯 C#。** v1 面板均为场景级；弹层生命周期策略随设置功能再定。
9. **启动流程与 Wwise 归属（2026-10-10，步骤 0 实施定案）**：MenuScene 为启动场景（Build 第 0 位）；WwiseGlobal 只留 MenuScene（AkInitializer 自带 DontDestroyOnLoad → 全程唯一实例），TrackPlayingScene 不放置（直接 Play 打歌场景无声音，调试从菜单进）；SampleScene 移出清单、保留文件。临时互跳脚本 `TempSceneJump`（F9/F10，`Assets/Features/UI/Scripts/Temp/`）供步骤 0 验证，步骤 6/7 接入正式流程后删除。
10. **选歌列表 = 切歌轮盘（2026-10-10，替代 ScrollRect 方案）**：交互 = 单一连续浮点"光标"驱动，格子视觉随距离映射（位置 / 缩放 / 透明度，越远越小越淡）；切歌 = 光标 tween 至目标整数（平滑但永远吸附）。"无限曲目" = 池化可见窗口实现真虚拟化（槽位表可配：默认 7 槽 = 选中 ±3，支持非对称如 3 上 4 下；池 = 静态槽位数 n + 1——过渡中点两端各有一个"透明在位"格，峰值为 n+1，多这一格使回收/换绑只需发生在"完全透明"的任意时刻，不要求精确时刻）；**无环绕**（首尾钳制）；**点击某格 = 切到该曲并居中**；位置指示条 = Scrollbar（Interactable 关闭，纯显示）；滚轮（IScrollHandler，需节流）/ ↑↓ 于步骤 4 接入。**动效库：步骤 4 动工前引入 DOTween（free 版，可进公开库）**——步骤 1~3 无动效需求，轻量推进不受影响。

11. **细节修订（2026-10-12，步骤 3 / 4a 定案）**：① 恢复上次选曲 = Presenter 按**引用反查** `SelectedTrack` 在列表中的索引（找不到回 0），不向 `TrackSelectionService` 加索引字段（索引是派生状态；"选中"语义 = 当前轮盘列表中的位置，未来排序 / 筛选后旧索引会失真）；难度恢复自 `SelectedChart.ChartDifficulty`。② 输入职责边界：原始输入捕获在 View（滚轮 `IScrollHandler` / ↑↓ `Update`，0.08s 节流），光标状态与首尾钳制在 Presenter。③ **DOTween 延后引入**（修订决策 10 的"步骤 4 前引入"）：手动"单浮点指数收敛"已同时满足平滑与吸附，待需要时长 / 缓动曲线级控制（详情区动效等）时再引入。④ `TempWheelTest` 已删除（2026-10-12）。

12. **难度选择落地定案（2026-10-12，步骤 4b）**：① 回退规则 = 就近向低（当前难度缺谱 → 先向低找最近，全无更低才向高）② 锁定态 v1 不做（只"有谱 / 无谱"两态；解锁机制后续版本）③ 详情区下推后置为步骤 4c ④ 难度按钮不用 Unity Button 组件——交互按 SongCell 模式（`IPointerClickHandler` 上抛 + 脚本自做过渡）；选中放大置顶 = `SetAsLastSibling` 调层级（嵌套 Canvas 方案显示异常已弃用；前提：难度栏不用 LayoutGroup、位置手摆）⑤ ←→ 切难度：无循环、缺谱自动跳过、0.08s 节流。

13. **详情区 / 开始流程落地定案（2026-10-12，步骤 4c / 6）**：① 详情区不含定数（定数只在难度栏按钮上）；文案 = `{曲名} Composer：{曲师} Charter：{谱师} BPM：{bpm}`，Charter 取当前选中难度的谱面（可随难度变化）；缺字段 / 无谱 / BPM≤0 占位「—」② 结构保持 Cover(Image) + 单 DetailText（TMP）不拆多对象；封面缺省 = sprite 清空 ③ **StartButton 例外保留 Unity Button 组件**（与 SongCell / 难度按钮的"不用 Button"约定不同，用户定案）——接法 = View 序列化 Button 引用 + Awake 里 `onClick.AddListener` 上抛 `StartClicked`，不用 Inspector 挂回调；整曲无谱时 `Button.interactable` 禁用 ④ 点开始直接 `LoadScene`（无过渡）⑤ 选曲不再预置默认值（`TrackAssetInstaller` 移除硬编码），由菜单写入；局内读取点（`TrackPlayingInstaller` / `TrackController.Start`）校验非空并报错。

14. **曲终回调与临时回程落地（2026-10-13；前置完成）**：① `TrackController` 本次只补结束回调——Post 掩码 OR `AK_EndOfEvent`，曲终触发 `public event Action TrackEnded`；主动 Stop / 场景拆场不触发（`_isStopping` 守卫），曲终清 `_playingID` 防重复 Stop；暂停 / 重开 / 时长不收口（结算"重试" = 重载场景，Wwise 清理走现有 OnDestroy Stop+Unload）② 步骤间桥 = 临时回程 `TempTrackEndReturn`（纯 C# IInitializable 订阅 `TrackEnded` → `LoadScene("MenuScene")`；`TrackPlayingInstaller` 一行绑定 NonLazy）——步骤 7 结算落地时删除，由 `ResultPresenter` 订阅接管 ③ 实机验证通过：曲终自动回菜单；F10 中途退出无重复加载、无报错。

15. **结算 UI 落地定案（2026-10-13，步骤 7）**：① 结算信息 = 单个 text 承载两来源（TrackSelectionService + ScoringSystem；格式用户自写，集中在 `ResultPresenter.BuildResultText()`）② 重试 / 返回按钮用 Unity Button（纯点击交互，Awake 接 onClick 上抛；**按钮选型原则归纳：纯点击 → Unity Button；需要"选中"类富状态 / 自定义过渡才走无 Button 模式（SongCell / 难度按钮）**——推翻早先"默认不用 Button"的粗略表述）③ 曲终残留 Note 不做清场（用户选 C：面板盖住；TrackTimeMs 归 0 会致残留 Note 回跳，面板全屏不透明则不可见）④ 阶段切换最小实现：不新增 Phase 枚举（ResultPresenter 独占曲终路径；显式阶段留待暂停步）⑤ 实机验证通过：曲终出结算 / 重试重开同曲 / 返回回菜单。

16. **打歌 HUD 落地定案（2026-10-13）**：① 进度数据源 = Wwise 音乐段落 `iPreEntryDuration + iActiveDuration`（`TrackController.TrackDurationMs`，运行时可靠；弃用 `AkUtilities.GetEventDurations`——默认实现恒返回 -1，构建不可靠）② `GameHudPresenter` = 首个 ITickable Presenter（每帧读、变化才下推；进度只前进不后退，曲终冻结在满）③ Combo 显示阈值（默认 2）收在 View 序列化字段 ④ 不做：暂停 / 动画 / 判定飘字。

17. **暂停功能落地定案（2026-10-13，无 UI 版）**：① 本步范围 = 暂停内核 + 临时测试按键（用户定向），面板 UI 顺延为下一步 ② 触发 = 旧输入系统 P 键（`PauseTestKey` 纯 C# ITickable 轮询切换，不碰正式输入映射；面板落地后删除）③ 阶段结构最小实现：不新增 Phase 枚举，状态收在 `TrackController`（`_isPaused` → 公开 `IsPaused`）；显式阶段继续顺延到面板步（输入路由 / 面板显隐出现真实消费者时再定）④ 暂停内核 = 单一时间源冻结：`Update` 在暂停时直接返回，TrackTimeMs 不推进（Note 视觉 / LaneView / 判定 / HUD 全部派生自它，一并停摆），暂停期间不采集轨道输入（不积压按键）；音乐用 `ExecuteActionOnPlayingID` 施加 Wwise `Pause / Resume`（位置保留续播，过渡时长 0；**坑：`ExecuteActionOnEvent` 首参是事件 ID——传 playingID 报 `Event ID not found` 且动作静默失效**，2026-10-13 首轮实机修复）⑤ 守卫：加载中（未开始）/ 已暂停 / 停止中不重复动作；曲终后 `_isStarted=false` 自然屏蔽 ⑥ 验证（2026-10-13 实机通过）：暂停音乐停 / 判定停 / 画面冻 / HUD 冻；续播原位；加载中与曲终后按 P 无效果；存量 Wwise 报错（`StopAndUnloadTrack` 同源 API 误用）一并消失。

18. **暂停面板 UI 定案（2026-10-13，动工实施中）**：① 按钮集 = 继续 / 重试 / 返回选歌（重试 = 重载打歌场景，同结算；返回 = MenuScene）② 恢复 = 3-2-1 倒数后续播（用户选定；倒数为 Resuming 独立阶段，走真实时间 `Time.deltaTime`，倒数期间音乐保持 Wwise Pause、ESC 忽略）③ 正式触发 = ESC 键（复用 `InputSystem.EscDown`——Back 动作已绑 `<Keyboard>/escape`；替换 P 键测试路径，PauseTestKey 面板落地即删）④ 阶段结构显式化 = TrackPhase 枚举（Loading / Playing / Paused / Resuming / Result），TrackController 单一持有并推进；取代 `_isStarted` / `_isPaused` / `IsPaused` / 公开 `Resume()`；Update 按阶段路由——非 Playing 一律不推时间源、不采输入，Resuming 只推倒数 ⑤ 副作用修订：Result 阶段冻结 TrackTimeMs（原归 0 致残留 Note 回跳，现回跳消除，HUD 进度停末值）⑥ 状态：全部完成并实机验证通过（2026-10-13）。首轮实机修复 = 暂停 → 重试卡 Loading（Wwise 延迟卸载队列 vs `LoadAsync` 静默不回回调；改同步 `Load()` + 卸载后即时 `DoUnloadBanks()` 冲刷，禁再改回异步，细节见进度节）

19. **输入模块重写定案（2026-10-15，设置功能前置；已完成）**：① 形态 = 纯 C# `InputService`（无 MonoBehaviour）+ SO 安装器 `InputInstaller` 绑 ProjectContext（仿 `PlayerConfigInstaller`）；废弃 `MonoSingletonHungry`/`Bootstrapper` 自拉起路径（三件套保留不删、零引用——用户定案）。② 消费范式 = 全轮询（判定热路径需"同一帧同一段代码读按键 + 读曲目时间"；重绑是唯一例外，走回调）。③ 语义 API = `LaneDown/Hold/Up`（lane ∈ -3..3）/ `BackDown`（原 EscDown）/ `MenuUp/Down/Left/Right` / `StartRebind` / `IsRebinding` / `ResetBinding` / `GetLaneKeyName`；`Menu*` 四动作加进 UIActions（资产与服务口同名）；模块位置 `Assets/Features/Input/`（`Settings/` = 资产+生成类，`Scripts/` = 代码），模块 README 同目录。④ 键位覆盖维持独立 `customBindings.json`（决策 6）：坏档备份 `.bak` 回默认 / 原子写 / 退出兜底（对齐 PlayerConfigStore）；修旧重绑缺陷（条件反转、`Start()` 后误 `Enable()` 时序）。⑤ 菜单键盘方向键迁入 `SongSelectPresenter`（ITickable 轮询 + 0.08s 节流随迁，键盘与滚轮共享节流）——"九"表"菜单输入源统一"完成；View 仅剩 UGUI 指针事件；`DifficultyStepRequested` 事件删除。⑥ `IsRebinding` 为设置弹层 / 重绑 UI 的 ESC 路由钩子；**重绑交互实机验证挂设置弹层步**（本轮未加临时测试键——2026-10-16 已随键位区闭环，见决策 20）。

20. **设置弹层落地定案（2026-10-16，含键位重绑；已完成并实机验证）**：① 生命周期 = **预实例化**（面板常驻场景、默认隐藏；View 手拖装配）；Factory 不做。② 弹层宿主 = `MenuNavigator` 长出单员（`OpenSettings` / `CloseTop` / `IsPopupOpen`；`SettingsPresenter` 在 Initialize 用 `AttachSettings` 注册——单向依赖破构造循环）；ESC 分发 = ITickable 轮询 `BackDown()`，ESC 与关闭按钮同一出口。③ 输入屏蔽双层：弹出层全屏 raycastTarget 背景挡鼠标 / 滚轮；`SongSelectPresenter` 五入口（点格 / 滚轮步进 / 点难度 / ←→ / 开始）查 `IsPopupOpen` 早退挡键盘。④ 数值调节：流速 / 偏移两行（标签 + 数值 + ± 单击步进，无长按；偏移带符号显示）；边界 / 步进常量收 `SettingsPresenter`（流速 500–2000 步 100、偏移 ±1000 步 10——2026-10-16 用户定稿）；写入即时生效（store 内存），关闭面板 `store.Save()` 检查点落盘。⑤ 键位重绑：KEYBINDS 区 7 行（拖引用顺序 = 轨道 -3 → +3；`KeybindRowView` 整行 Unity Button）；点行 → 独立"请按键"提示弹层（`GameObject.SetActive` 开关、覆盖面板兼挡鼠标）→ `InputService.StartRebind`；完成 / 取消同一出口刷新该行；`RESET KEYS` = 全局 `ResetBinding` + 刷新。⑥ 冲突检测 = **不做**（"九"表 + 现有代码问题 5）：一键多轨为规则内合法，不构成冲突。⑦ ESC 吞噬修复：捕获中按 ESC 取消重绑的同一帧，取消回调先于消费者轮询执行 → `InputService.BackDown()` 在"捕获中 + 取消帧"自抑制（导航器重复守卫删除，语义单一来源）；验证点 = 捕获中 ESC 只取消、弹层不关。⑧ `CancelRebind()` 未加——提示层挡鼠标后无调用方。

21. **曲目 / 音频重构与统一预览（2026-11-13，已完成）**：① 数据三层 = 官谱 SO `OfficialTrackInfo/OfficialChartInfo` + 自制谱 `TrackInfo.txt`（Shared DTO）→ `OfficialTrackAdapter/CustomTrackLoader` → 运行时 `TrackData/ChartData`；菜单 / 会话 / 打歌只消费标准类，`TrackCollection` 唯一入口（`TrackSelectionService` 实际落地为 `PlaySessionService`）。② 自制谱目录 = `<exe>/CustomTracks`（编辑器工程根目录，不入库）；Wwise External Source 事件 `PlayCustomMusic`（cookie `External_Source`、codec PCM_WAV）。③ 预览统一 = `PreviewStart/End`，主音乐 Seek + 循环，删除 `PreviewMusic`；独立预览 emitter + 音量渐变（`PreviewFadeMs`）：切换淡出 → 淡入、循环终点前淡出 → Seek → 淡入。④ 正 StartOffset = 静音段墙钟 + 首次 `GetSourcePlayPosition` 校准 bias 无缝交接。⑤ 存档：成绩键 = `乐曲名_难度代号_存档签名`（签名为 track 级 `ArchiveSignature`，官谱 SO 手填、可空，空保留末尾 `_`）；自制谱签名留空，同名曲共用键（已知限制）。

22. **官谱分类数据层（2026-11-13，数据层已完成；UI 未做）**：① 集中配置 = `OfficialTrackCategoryConfig` SO（`OfficialCategoryEntry` 列表 = 分类 id + 曲目列表），成为官谱唯一来源；`TrackServiceInstaller` 引用该 SO 传入 `TrackCollection`，删除 `_tracks`，不向单曲 SO 加字段。② `OfficialCategory` 枚举仅作分类 id（后续章节展示匹配），不参与排序；聚合顺序 = config 条目顺序；config 按整数序列化枚举，新增分类只能末尾追加。③ `TrackCollection` 查询 = `GetTracks` / `GetOfficialTracks` / `GetOfficialTracks(OfficialCategory)` / `GetCustomTracks`；分类栏的分类集合与顺序由 UI 自管，数据层不提供分类枚举列表。④ 规则 = `None` 跳过、同分类多条 entry 第一条生效（后续整条忽略 + 警告）、同曲重复归属首处生效、空分类返回空列表。

---

## 九、待定事项与后续版本（实现到相关功能时先与用户确认）

| 事项 | 关联 | 何时必须定 |
| --- | --- | --- |
| 启动场景 / WwiseGlobal 归属方案 | 现有代码问题 4 | 已定案（2026-10-10，见决策 9） |
| 难度回退规则 | 决策 3 | 已定案：就近向低（2026-10-12，决策 12） |
| 难度解锁数据来源（成绩存档？） | 决策 3 | 后续版本；v1 不做锁定态（2026-10-12 定案，决策 12）；数据来源将由「分数存档系统接入」步铺（2026-10-13 列下一步） |
| 详情区下推与刷新 | 步骤 4c | 已定案并完成（2026-10-12）：详情区不含定数；文案含曲名 / Composer / Charter / BPM；见决策 13 |
| StartButton 交互模式 | 步骤 6 | 已定案并完成（2026-10-12）：保留 Unity Button 组件（例外）；见决策 13 |
| 结算面板字段清单 / 按钮交互 | 步骤 7 | 已定案并完成（2026-10-13）：单 text 两来源 + Unity Button；见决策 15 |
| 暂停菜单面板规格（按钮集 / 恢复倒数 / 正式触发 / 阶段结构显式化） | 暂停功能 | 已定案并完成（2026-10-13，见决策 18） |
| 菜单输入源统一（`SongSelectView` 用旧 Input 轮询 ↑↓←→，未走 `InputSystem`/新输入系统；与打歌、ESC 路由不一致） | 步骤 5 收口顺带发现（2026-10-13） | 已定案并完成（2026-10-15）：键盘方向键经 `InputService` 轮询、节流随迁；重绑 UI 本体仍后置；见决策 19 |
| `NoteJudgeConfig`（SO）与 `SettingsService` 的关系（倾向：SO 只放默认值/上限） | 决策 5 | 已定案（2026-10-16）：v1 不引 SO——数值边界 / 步进收 `SettingsPresenter` 常量（要调改代码）；见决策 20 |
| 弹层生命周期策略（预实例化 vs Factory） | 决策 1、3.3 | 已定案并完成（2026-10-16）：预实例化；见决策 20 |
| 键位重绑冲突检测做不做 | 现有代码问题 5 | 已定案并完成（2026-10-16）：不做检测——一键多轨为规则内合法；见决策 20 |
| 曲目试听接口接入 | 决策 4 | 已完成（2026-11-13，见决策 21） |
| 节拍辅助校准页 | 决策 5 | 后续版本 |
