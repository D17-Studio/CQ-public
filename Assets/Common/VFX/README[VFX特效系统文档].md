# VFX 特效系统文档

`Assets/Common/VFX`

基于 Unity ParticleSystem 的集中式特效系统。特效数据存放在 prefab 的 `VfxEffectDefinition` 组件上，运行时由常驻单例 `VfxRuntime` 统一负责加载、池化、并发预算、冷却与事件规则调度；业务代码通过 Zenject 注入的 `VfxService` 播放。

- 数据来源：`VfxCatalog`（Resources 资产）只保存 prefab 清单与规则；每个特效的 cueId、通道、时长、预算、能力、参数声明都在 prefab 根节点的 `VfxEffectDefinition` 上，**prefab 是特效数据的唯一来源**。
- 调用入口：`VfxService`，支持直接 `Play(VfxRequest)` 和事件驱动的 `Signal(VfxSignal)` 两条路径。
- 运行时自动完成：对象池、并发上限、优先级抢占、冷却、通道排序、强度/染色/透明度/时间缩放、参数下发、统计。

---

## 一、特效编辑与调用

### 1.1 快速上手

新增一个特效只需要三步：

1. 制作 prefab，根节点挂 `VfxEffectDefinition`，填好 `cueId` 等字段。
2. 把 prefab 拖进 `Assets/Common/VFX/Resources/VfxCatalog.asset` 的 `prefabs` 列表。
3. 业务代码注入 `VfxService`，用 `Play(new VfxRequest(cueId)...)` 播放。

不需要写任何新代码，也不需要改 Catalog 代码。

### 1.2 编辑特效 Prefab

#### 1.2.1 根节点：VfxEffectDefinition

prefab 根节点必须且只能挂一个 `VfxEffectDefinition`（`[DisallowMultipleComponent]`）。运行时启动时会遍历 Catalog 的每个 prefab，取根节点上的该组件建立 `cueId → 定义` 字典。

| 字段 | 说明 |
|------|------|
| `cueId` | 稳定且全局唯一的 ID，调用方用它指定特效。**改名等价于换了一个特效**，所有调用点必须同步修改。空值或重复会在启动时报错并跳过该 prefab。 |
| `displayName` | 展示名，仅供编辑器和调试辨认，不参与运行逻辑。 |
| `channel` | 默认通道，请求可用 `InChannel()` 覆盖。见 2.9。 |
| `lifecycle` | `OneShot` / `Timed` / `Sustained`。目前运行时只对 `Sustained` 做特殊处理（无限时长，必须手动停止）；`OneShot` 与 `Timed` 的运行时行为相同，区别仅为语义。 |
| `duration` | 秒。`OneShot` / `Timed` 的持续时间，`Sustained` 忽略。填 0 或负数时，在**每次播放时**按粒子系统自动估算（见 2.7）。 |
| `priority` | 并发抢占时的优先级基准，数值越大越"重要"。不是排序层级，也不影响粒子渲染顺序。 |
| `maxConcurrent` | 同一 cueId 同时活跃的实例上限，`0` 表示不限。 |
| `cooldown` | 同一 cueId 两次成功播放之间的最小间隔（秒），`0` 表示关闭。规则可覆盖，见 2.14。 |
| `capabilities` | 能力白名单，声明调用方可以覆盖哪些旋钮。未声明的能力，请求值会被忽略。默认 `Intensity | Tint | TimeScale`（不含 `Opacity`）。 |
| `essential` | 标记为"必要特效"：无障碍减弱动效时不降强度，也不会被并发抢占。 |
| `parameters` | 参数声明数组，见 1.2.3。 |

编辑时 `OnValidate` 会把 `duration`、`maxConcurrent`、`cooldown` 限制为非负。

#### 1.2.2 粒子系统结构

- 根节点之下可以放任意层级、任意数量的 `ParticleSystem`（包括未激活的子对象）。实例化时用 `GetComponentsInChildren<ParticleSystem>(true)` 一次性收集。
- 首次创建池化实例时会记录每个粒子系统的原始值：`startColor`、`startSizeMultiplier`、`startSpeedMultiplier`、`simulationSpeed`、`emission.rateOverTimeMultiplier`，以及每个 `ParticleSystemRenderer.sortingOrder`。每次播放都从这些原始值重新计算，因此特效不会因为上一次请求的强度/染色而"残留"。
- 播放时对所有系统执行 `Clear(true)` + `Play(true)`。
- `ParticleSystemRenderer` 的 Sorting Layer 由 prefab 手动设置，运行时**只**追加通道对应的 `sortingOrder` 偏移（见 2.9），不会改 Layer。
- prefab 根节点即特效锚点：播放时根节点的世界位置/旋转由请求决定。
- prefab 会被克隆并池化复用，不要在上面挂依赖场景对象的逻辑；`VfxEffectDefinition` 本身只是数据组件。

#### 1.2.3 参数声明与参数消费

参数用于把调用方的动态值下发到粒子系统。分两步：

**第一步：在 `VfxEffectDefinition.parameters` 声明**

每个 `VfxParamDeclaration` 包含：

| 字段 | 说明 |
|------|------|
| `name` | 参数名，区分大小写。 |
| `type` | `Float` / `Color` / `Bool`。 |
| `defaultFloat` / `defaultColor` / `defaultBool` | 未提供覆盖时的默认值。 |
| `minValue` / `maxValue` | 仅对 Float 生效，解析时自动 Clamp。 |

示例（`FX_NoteImpact`）：声明一个 Color 参数 `color`，默认黄色 `(1, 0.85, 0.2)`。

**第二步：在需要消费参数的粒子系统上挂 Sink**

Sink 是实现 `IVfxParameterSink` 的 MonoBehaviour，必须挂在实际要受影响的 `ParticleSystem` 所在 GameObject 上。实例化时会扫描整棵子树收集所有 Sink，并在每次播放前调用 `ApplyParameters(VfxParamValues)`。

系统自带 `VfxSelfColorSink`（`[RequireComponent(ParticleSystem)]`）：读取名为 `color` 的参数，写到**自己这个**粒子系统的 `main.startColor`。它用于组合特效中只给某一个子系统染色，避免整体 Tint 影响其他系统。

`FX_NoteImpact` 的用法：`FastRing` 子对象挂 `VfxSelfColorSink`，`Burst` 和 `SlowRing` 不受 `color` 参数影响。

自定义 Sink 示例：

```csharp
public sealed class MyVfxScaleSink : MonoBehaviour, IVfxParameterSink
{
    [SerializeField] ParticleSystem target;

    public void ApplyParameters(VfxParamValues values)
    {
        var main = target.main;
        main.startSizeMultiplier = values.GetFloat("scale", 1f);
    }
}
```

注意：

- 只有实例中存在至少一个 Sink 时，运行时才会解析并下发参数；没有 Sink 的特效不会产生这部分开销。
- 参数默认值也会被解析，因此 Sink 每次播放都会收到一份完整的声明值 + 合法覆盖值。
- 未声明或类型不匹配的覆盖会被忽略，开发期会输出一次警告（见 2.13）。

### 1.3 注册到 Catalog

`Assets/Common/VFX/Resources/VfxCatalog.asset` 由 `Resources.Load<VfxCatalog>("VfxCatalog")` 按固定名加载，**路径和文件名不要随意改**，否则运行时启动会报错。

- `prefabs`：特效 prefab 清单。启动时逐个读取根节点的 `VfxEffectDefinition`。
- `rules`：事件规则列表，见 1.4.5。当前项目为空。

启动时会检查并报错的情况（该 prefab 会被跳过）：

- 数组元素为 null
- prefab 根节点没有 `VfxEffectDefinition`
- `cueId` 为空
- `cueId` 重复

Catalog 只在 `VfxRuntime.Awake` 时读取一次。运行中修改 Catalog 或 prefab 不会生效，需要重新进入播放模式。

### 1.4 在代码中调用

#### 1.4.1 获取 VfxService

`VfxService` 由 `VfxInstaller` 绑定为 Zenject 单例，常规用法是字段/构造注入：

```csharp
[Inject] private VfxService _vfxService;
```

本项目现有做法：`NoteViewSpawner` 注入 `VfxService`，在 `Initialize(...)` 时传给池化的 `NoteView` 子类使用。

`VfxRuntime` 不提供静态访问，业务代码统一通过注入的 `VfxService` 调用。

#### 1.4.2 Play 重载

```csharp
// 最简：cueId + 世界坐标
_vfxService.Play("fx_note_impact", worldPos);

// 跟随目标（默认 FollowFull）
_vfxService.PlayAt(noteTransform, "fx_note_impact");

// 完整请求
_vfxService.Play(new VfxRequest("fx_note_impact")
    .At(worldPos)
    .WithParam(VfxParamValue.MakeColor("color", tint)));
```

返回值是 `VfxHandle`，播放失败（未知 cue、冷却中、并发已满等）时返回 `null`。

#### 1.4.3 VfxRequest 链式 API

| 方法 | 作用 |
|------|------|
| `At(Vector3)` | 世界坐标。 |
| `Facing(Quaternion)` | 世界旋转，默认 `identity`。 |
| `Follow(Transform, VfxAttachMode)` | 跟随目标。默认 `FollowFull`，同时把位置设为目标当前位置。 |
| `InChannel(VfxChannel)` | 覆盖定义里的通道。 |
| `WithPriority(int)` | 覆盖定义里的优先级，参与并发抢占判断。 |
| `WithIntensity(float)` | 强度，默认 1。需声明 `Intensity` 能力。 |
| `WithTint(Color)` | 染色，需声明 `Tint` 能力。 |
| `WithOpacity(float)` | 透明度，默认 1。需声明 `Opacity` 能力。 |
| `WithTimeScale(float)` | 时间缩放，默认 1。需声明 `TimeScale` 能力。 |
| `WithParam(VfxParamValue)` | 追加参数覆盖，可多次调用；同名参数后写覆盖前写。 |

请求不包含时长字段，时长完全由 `VfxEffectDefinition.duration` 决定。

#### 1.4.4 参数覆盖

```csharp
VfxParamValue.MakeFloat("scale", 1.5f);
VfxParamValue.MakeColor("color", new Color(0.35f, 0.7f, 1f));
VfxParamValue.MakeBool("glow", true);
```

- 只有定义里声明过且类型一致的参数才会生效，Float 会自动 Clamp 到 `[minValue, maxValue]`。
- 参数与能力（Intensity/Tint/Opacity/TimeScale）是两套独立机制：参数走 Sink，能力走粒子系统主模块，可同时使用。

#### 1.4.5 事件规则调用 Signal（进阶）

当"什么事件播什么特效"希望配置化而不是写死在业务代码里时，用 `Signal`：

```csharp
_vfxService.Signal(new VfxSignal("note_hit", worldPos)
    .From(noteTransform)
    .With("judge", "Perfect")
    .With("combo", 12));
```

- `VfxSignal` 内置字段：`type`、`position`、`owner`；`With(key, value)` 追加自定义字段。
- 运行时按 `eventType == signal.type` 取出该事件的所有规则，按规则 `priority` 降序逐条匹配（同优先级保持声明顺序）。
- 规则的所有 `conditions` 必须同时满足（AND，无 OR）。
- 命中后由规则设置锚点、跟随方式，并把 signal 字段映射到请求参数。
- `exclusive` 的规则被命中后不再处理后续规则（即使这条规则播放失败，也会中断）。
- 返回成功播放的条数。

当前项目 `rules` 为空，业务侧直接调用 `Play`，没有使用 Signal。

#### 1.4.6 句柄、停止与状态

```csharp
var handle = _vfxService.Play(request);
if (handle != null && handle.IsValid)
{
    handle.Stop();          // 立即结束并回池
}

_vfxService.StopAll();      // 停止所有活跃特效
```

- `Sustained` 特效不会自动结束，必须保存句柄调用 `Stop()`，或统一 `StopAll()`。
- 句柄在实例回池后失效，`IsValid` 变为 false。
- `VfxService.Stats` 返回当前统计快照（活跃数、池中数、累计播放/拒绝/未知 cue/冷却抑制/抢占次数），见 2.13。
- `VfxService.ReducedMotion` 控制无障碍"减弱动效"：非 `essential` 特效的强度乘以 0.4。目前项目还没有把它接到设置界面。

### 1.5 编辑与调用检查清单

- [ ] prefab 根节点有且只有一个 `VfxEffectDefinition`
- [ ] `cueId` 非空、全局唯一、调用方拼写一致
- [ ] 粒子系统和 Sink 都放在根节点子树内
- [ ] 需要染色的子系统挂 `VfxSelfColorSink`，且 `color` 参数已声明
- [ ] prefab 已加入 `VfxCatalog.prefabs`
- [ ] 需要的 `capabilities` 已勾选
- [ ] `Sustained` 特效的调用方负责 `Stop`
- [ ] 进播放模式确认没有 `[VFX] ...` 报错

---

## 二、系统结构说明

### 2.1 目录结构

```
Assets/Common/VFX
├── Installers/
│   ├── VfxInstaller.cs            Zenject ScriptableObjectInstaller（Assembly-CSharp）
│   └── VfxInstaller.asset         注册进 ProjectContext 的 installer 资产
├── Scripts/
│   ├── VFX.Runtime.asmdef         独立程序集，命名空间 VFXTest.Vfx
│   └── Core/
│       ├── VfxTypes.cs            公共类型：枚举、请求、信号、规则、统计
│       ├── VfxCatalog.cs          Resources 资产：prefab 清单 + 规则 + 规则索引
│       ├── VfxEffectDefinition.cs prefab 根节点数据组件
│       ├── VfxRuntime.cs          容器单例：播放管线、池化、抢占；含 VfxInstance、VfxHandle
│       ├── VfxService.cs          对业务暴露的门面
│       ├── VfxRuleEvaluator.cs    Signal 规则匹配与字段映射
│       ├── VfxParamResolver.cs    参数解析；含 VfxParamValues、IVfxParameterSink
│       └── VfxSelfColorSink.cs    内置参数 Sink（只染自己所在的粒子系统）
├── Resources/
│   └── VfxCatalog.asset           固定名 VfxCatalog
├── Prefabs/
│   └── FX_NoteImpact.prefab       当前唯一的特效
├── Materials/                     示例材质（M_AdditiveSpark、M_RingAdditive）
└── Textures/                      示例贴图（T_Spark、T_ring）
```

`VfxInstaller.cs` 位于 `Scripts` 之外，不在 `VFX.Runtime.asmdef` 管辖范围内，因此可以引用 Zenject；`VFX.Runtime` 程序集本身不依赖 Zenject。

### 2.2 分层

```
业务层（NoteView / 其他功能）
    │ 注入
    ▼
接入层   VfxService ─── VfxInstaller（Zenject 绑定）
    ▼
运行时   VfxRuntime（DontDestroyOnLoad 单例）
    ├── VfxInstance（一个池化播放实例）
    ├── 对象池（每个 cue 一个 Stack<VfxInstance>）
    ├── 通道根节点 Channels/{Background, Gameplay, Foreground, Screen, CameraPost}
    └── 统计 VfxStats
    ▼
数据层
    ├── VfxCatalog（Resources 资产）
    ├── VfxEffectDefinition（每个 prefab 根节点的数据）
    ├── VfxRuleEvaluator（事件规则）
    └── VfxParamResolver（参数默认值 + 覆盖）
```

类职责：

| 类型 | 职责 |
|------|------|
| `VfxService` | 业务门面。转发 Play/Signal/StopAll/Stats/ReducedMotion，持有 `VfxRuntime` 引用。 |
| `VfxInstaller` | ProjectContext 的 Zenject installer。通过容器创建并绑定 `VfxRuntime`，再绑定 `VfxService`。 |
| `VfxRuntime` | 容器单例宿主与调度核心：加载 Catalog、建立定义表、通道根节点、播放校验、冷却、并发、池化、逐帧 Tick、统计。 |
| `VfxInstance` | 单个池化实例：克隆 prefab、缓存原始粒子参数、应用能力/参数、跟随、寿命计时、释放。 |
| `VfxHandle` | 播放句柄：`IsValid` / `CueId` / `Stop()`。 |
| `VfxCatalog` | ScriptableObject：prefab 数组 + 规则数组；懒构建按事件分组的规则索引。 |
| `VfxEffectDefinition` | prefab 数据组件：cueId、通道、生命周期、预算、能力、参数声明。 |
| `VfxRuleEvaluator` | 判断规则是否匹配信号，并把信号字段映射到请求。 |
| `VfxParamResolver` | 合并声明默认值与调用方覆盖，做类型/范围校验。 |
| `VfxParamValues` | 解析后的参数字典，供 Sink 读取。 |
| `VfxSelfColorSink` | 内置 Sink：把 `color` 参数写到自身粒子系统。 |
| `VfxSignal` | 事件信号：type、position、owner + 自定义字段。 |
| `VfxRequest` | 播放请求：cue、位置/旋转/跟随、通道、优先级、能力值、参数覆盖。 |

### 2.3 启动与装配

```
Zenject ProjectContext（首次 SceneContext 初始化时）
└→ VfxInstaller.InstallBindings()
   ├→ Container.Bind<VfxRuntime>()
   │    .FromNewComponentOnNewGameObject()
   │    .WithGameObjectName("[VfxSystem]")
   │    .UnderTransform(ProjectContext.transform)
   │    .AsSingle().NonLazy()
   │    └→ Awake()
   │       ├→ Resources.Load<VfxCatalog>("VfxCatalog")
   │       ├→ BuildDefinitions()      // prefab 根 VfxEffectDefinition → cueId 字典
   │       └→ CreateChannelRoots()    // [VfxSystem]/Channels/<Channel>
   └→ Container.Bind<VfxService>().AsSingle()   // 构造注入 VfxRuntime
```

- `[VfxSystem]` 由容器创建并挂在 ProjectContext（DontDestroyOnLoad）下，随容器跨场景常驻。
- `VfxRuntime` 只存在一个容器实例（`AsSingle` + `NonLazy`），没有静态 `Instance`，也没有运行时自动创建逻辑。
- `VfxInstaller.asset` 已注册在 `Assets/Resources/ProjectContext.prefab` 的 `_scriptableObjectInstallers` 列表中。
- 若场景没有 ProjectContext，VFX 不会被初始化。

### 2.4 运行时对象模型

```
ProjectContext（DontDestroyOnLoad）
└── [VfxSystem]
    ├── VfxRuntime
    └── Channels
        ├── Background
        ├── Gameplay
        │   ├── fx_note_impact (pooled)     ← 活跃或池中实例
        │   └── ...
        ├── Foreground
        ├── Screen
        └── CameraPost
```

主要运行时容器：

| 容器 | 含义 |
|------|------|
| `active` | 当前活跃实例列表，逐帧 Tick。 |
| `pools` | `cueId → Stack<VfxInstance>`，每 cue 一个池。 |
| `activeByCue` | 每 cue 当前活跃数量，用于 `maxConcurrent`。 |
| `definitions` | `cueId → VfxEffectDefinition`，启动时构建一次。 |
| `lastSpawnTime` | 每 cue 上次成功播放的 `Time.time`，用于冷却。 |
| `warnedKeys` | 开发期告警去重。 |
| `ruleBuffer` | 复用的事件规则查询缓冲，避免分配。 |

### 2.5 Play 播放管线

`VfxService.Play → VfxRuntime.Play → PlayInternal`：

1. 校验 `request`、`cueId` 非空。
2. 从 `definitions` 查定义；查不到记 `unknownCues` 并输出一次警告。
3. 检查定义对应 prefab 是否存在。
4. 检查冷却：取规则覆盖值或 `definition.cooldown`；`Time.time - lastSpawnTime < cooldown` 时抑制，记 `cooldownSuppressed` 并返回。
5. 检查并发：`maxConcurrent > 0` 且同 cue 活跃数已满时尝试抢占（见 2.8）；抢不到记 `rejected` 并返回。
6. 开发期校验：未声明的能力/参数、参数类型不匹配，输出一次警告。
7. 解析通道：请求通道优先，非法值回退 `Gameplay`。
8. 从池中取实例；池空则 `Instantiate` 一个新实例并缓存原始粒子值。
9. `VfxInstance.Play`：激活、设置位置/旋转/跟随、应用通道排序偏移、应用能力、解析并下发参数、播放粒子。
10. 创建 `VfxHandle`，加入 `active`，更新活跃计数、上次播放时间、`played` 统计，返回句柄。

### 2.6 Signal 规则管线

`VfxService.Signal → VfxRuntime.Signal`：

1. `Catalog.GetRules(signal.type, buffer)`：按事件类型取规则（构建索引时已按 `priority` 降序稳定排序）。
2. 逐条 `VfxRuleEvaluator.Matches`：`eventType` 必须相等，所有条件 AND。
3. 命中后创建 `new VfxRequest(rule.cueId)`，`ApplyTo`：
   - `anchorSource` 决定锚点用 `signal.position` 还是 `signal.owner.position`；
   - `attachMode != WorldFixed` 时把 `signal.owner` 设为跟随目标（owner 为空则回退 WorldFixed）；
   - 按 `mappings` 把信号字段写入请求（见 2.14）。
4. 走 `PlayInternal`，冷却使用 `rule.cooldownOverride`。
5. `exclusive` 规则处理完立即 break。
6. 返回成功播放数。

### 2.7 生命周期与时长

| lifecycle | 运行时行为 |
|-----------|-----------|
| `OneShot` | 播放一次，到达时长后自动结束回池。 |
| `Timed` | 与 `OneShot` 当前实现相同，仅语义区分。 |
| `Sustained` | `duration = +∞`，不会自动结束，必须 `handle.Stop()` 或 `StopAll()`。 |

时长解析（`VfxInstance.ResolveDuration`）：

1. `definition.duration > 0` → 直接使用。
2. 否则取所有**非循环**粒子系统中 `main.duration + main.startLifetime.constantMax` 的最大值。
3. 结果大于 0.05 秒则使用，并上限 30 秒；否则回退 2 秒。

时长完全来自定义，请求不能覆盖。`ReducedMotion` 只降低强度，不改变时长。

### 2.8 并发、冷却与抢占

- `maxConcurrent` 按 cueId 计数，`0` 为不限。
- 满额时，运行时会寻找一个可抢占的受害者：
  - 同一 cueId、非 `essential`；
  - 其 `definition.priority` **严格小于**本次请求的优先级（请求优先级 = `request.priority ?? definition.priority`）；
  - 在候选者中取优先级最低的一个。
- 找到受害者则立即结束并回池（**没有淡出**），记 `preempted`，然后继续播放。
- 找不到受害者则本次播放被拒绝，记 `rejected`。
- `essential` 特效既不会被抢占，也不受减弱动效影响。
- 冷却在每次成功播放时刷新；被冷却或并发拒绝的尝试不会刷新冷却。
- 冷却检查发生在并发检查之前。

### 2.9 通道与渲染排序

通道只决定两件事：特效挂在哪个通道根节点下、`ParticleSystemRenderer.sortingOrder` 追加多少偏移。

| 枚举 | 值 | sortingOrder 偏移 | 用途 |
|------|----|------------------|------|
| `Background` | 0 | -1000 | 背景 |
| `Gameplay` | 1 | 0 | 玩法层（默认） |
| `Foreground` | 2 | +500 | 前景 |
| `Screen` | 3 | +1000 | 屏幕层 |
| `CameraPost` | 4 | +1500 | 屏幕后处理层 |

- 偏移叠加在 prefab 里手设的原始 `sortingOrder` 上：`最终值 = 原始值 + 偏移`。
- Sorting Layer 不由运行时管理，仍在 prefab 的 `ParticleSystemRenderer` 上手设。
- `CameraPost` 目前只体现为排序偏移，没有额外的相机后处理绑定逻辑。

### 2.10 能力（Capabilities）应用细节

每次播放都会从缓存的原始粒子值重新计算，所以请求之间互不污染。

| 能力 | 实现方式 | 取值范围 |
|------|----------|----------|
| `Intensity` | `startSizeMultiplier = 原始 × I`；`startSpeedMultiplier = 原始 × Lerp(1, I, 0.5)`；`emission.rateOverTimeMultiplier = 原始 × I` | I 钳制到 `[0.05, 4]` |
| `Tint` | 把 `startColor` 的 RGB 与 tint 相乘（alpha 也乘 tint.a） | - |
| `Opacity` | 把 `startColor` 的 alpha 乘以 opacity | `[0, 1]` |
| `TimeScale` | `simulationSpeed = 原始 × T` | T 钳制到 `[0, 4]` |

- 未声明的能力：请求值被忽略，开发期输出一次警告。
- 染色/透明度只对 `startColor` 的 **Color** 和 **TwoColors** 两种 Gradient Mode 生效；`Gradient` / `TwoGradients` / `RandomColor` 模式不会被修改。
- 只有实际提供了 tint 或 opacity ≠ 1 时才修改颜色，否则恢复原始颜色。
- 强度对尺寸和发射率是线性缩放，对速度只按一半比例缩放（视觉上更自然）。

### 2.11 参数解析与分发

```
VfxInstance.Play
  └→ 若实例中存在 Sink：
       VfxParamResolver.Resolve(definition.parameters, request.parameters)
         ├→ 先写入所有声明默认值（Float 做 Clamp）
         └→ 再写入覆盖值（必须已声明且类型一致；Float 做 Clamp）
       └→ 依次调用每个 Sink.ApplyParameters(values)
```

- 名称比较使用 Ordinal（区分大小写）。
- 未声明、类型不匹配的覆盖直接忽略，开发期告警。
- 所有 Sink 收到同一份 `VfxParamValues`。
- 内置 `VfxSelfColorSink` 只处理 `color` 参数；未提供时使用 Sink 自己的 `defaultColor`。

### 2.12 池化与资源驻留

- 每个 cue 独立对象池；首次播放时按需 `Instantiate`，之后复用。
- 池中实例挂在对应通道根节点下并 `SetActive(false)`；取出时重新设定父节点、位置与旋转。
- 池大小没有上限，也没有自动收缩；峰值并发决定实例数量。
- `[VfxSystem]` 挂在 ProjectContext 下，连同所有池化实例**跨场景存活**。切场景时如有残留特效（尤其是 `Sustained`），需要调用方主动 `StopAll()`。
- 实例回池时执行 `Stop(StopEmittingAndClear)` 并清空跟随状态、句柄。

### 2.13 统计与诊断

`VfxService.Stats`（`VfxStats`）：

| 字段 | 含义 |
|------|------|
| `active` | 当前活跃实例数。 |
| `pooled` | 当前池中实例总数。 |
| `played` | 累计成功播放次数。 |
| `rejected` | 累计被拒绝的播放次数（并发已满、实例获取失败等）。 |
| `unknownCues` | 累计未知 cue / 无 prefab 的次数。 |
| `cooldownSuppressed` | 累计因冷却被抑制的次数。 |
| `preempted` | 累计抢占次数。 |

计数器从 `VfxRuntime` 创建起累计，不随场景重置（除非整个运行时被销毁）。

开发期告警（`Application.isEditor || Debug.isDebugBuild`）包括：

- Catalog 缺失、prefab 为 null、缺少 `VfxEffectDefinition`、`cueId` 为空/重复（启动时 `LogError`）
- 未知 cue、无 prefab
- 未声明的能力被请求
- 未声明/类型不匹配的参数覆盖

除启动错误外，其余警告通过 `WarnOnce` 按 key 去重，每个运行时生命周期只输出一次。正式包中不会输出这些警告。

### 2.14 规则数据结构（VfxRuleEntry）

规则配置在 `VfxCatalog.rules`，目前无自定义编辑器，直接在 Inspector 中编辑数组。

| 字段 | 说明 |
|------|------|
| `id` | 规则标识，仅调试用。 |
| `eventType` | 匹配 `signal.type`，区分大小写。 |
| `cueId` | 命中后播放的特效。 |
| `priority` | 同事件规则排序，降序；相同优先级保持声明顺序。 |
| `exclusive` | 命中后中断后续规则。 |
| `anchorSource` | `SignalPosition`（用信号位置）或 `SignalOwner`（用 owner 位置）。 |
| `attachMode` | `WorldFixed` / `FollowPosition` / `FollowFull`；非 WorldFixed 时跟随 `signal.owner`。 |
| `cooldownOverride` | `< 0` 使用定义冷却；`0` 关闭冷却；`> 0` 覆盖冷却秒数。 |
| `conditions` | 条件数组，全部满足才命中（AND）。 |
| `mappings` | 字段映射数组。 |

条件比较符：`Equals`、`NotEquals`、`Greater`、`GreaterOrEqual`、`Less`、`LessOrEqual`、`Exists`、`NotExists`。条件字段来自信号内置字段（`type` / `position` / `owner`）或 `With` 追加的键；空字段名视为恒真。数值比较使用不区分区域性的浮点解析，字符串比较不区分大小写。

映射目标（`target`，不区分大小写）：

| target | 效果 |
|--------|------|
| `intensity` | 写入请求强度。 |
| `opacity` | 写入请求透明度。 |
| `timescale` | 写入请求时间缩放。 |
| `priority` | 写入请求优先级（四舍五入为 int）。 |
| `tint` / `color` | 写入请求染色。 |
| `param:<name>` | 按源值类型推断并追加参数：Color → Color，bool → Bool，数值 → Float。 |

映射源（`source`）是信号字段名；源值不存在或为 null 时跳过该条映射。

---

## 三、当前项目接入现状（2026-11-13）

- 特效：仅 `fx_note_impact`（`Prefabs/FX_NoteImpact.prefab`）。
  - 通道 `Gameplay`，`OneShot`，时长 0.4s，`maxConcurrent = 40`，`priority = 0`，冷却 0。
  - `capabilities = -1`（所有能力）。
  - 声明参数 `color`（Color，默认黄色）。
  - 子对象：`Burst`、`FastRing`（挂 `VfxSelfColorSink`）、`SlowRing`。
- Catalog：`prefabs` 只含上述 prefab，`rules` 为空。
- 业务调用：`DotView` / `DashView` / `MuteView` 在 `PlayHitEffect` 中调用 `VfxService.Play(new VfxRequest("fx_note_impact").At(worldPos).WithParam(...color...))`；`VfxService` 由 `NoteViewSpawner` 注入并传给池化的 NoteView。
- 三个 NoteView 里计算了 `intensity` 局部变量，但目前没有传给请求（预留）。
- `Signal` 规则路径、`ReducedMotion` 开关均已实现，但尚未被玩法或设置界面使用。

---

## 四、常见问题与排错

| 现象 | 原因 | 处理 |
|------|------|------|
| 启动报 `Resources/VfxCatalog was not found` | Catalog 不在 Resources 或名字被改 | 保持 `Assets/Common/VFX/Resources/VfxCatalog.asset` |
| 完全没有 `[VfxSystem]`，VFX 不工作 | 场景没有 ProjectContext，或 `VfxInstaller.asset` 未注册 | 检查 `Assets/Resources/ProjectContext.prefab` 的 installer 列表 |
| 业务播放无效果，控制台 `Unknown cue` | cueId 拼错 / prefab 没进 Catalog / 启动检查失败被跳过 | 核对 Catalog 与 `cueId`，重启播放模式 |
| 启动报 `Prefab ... has no VfxEffectDefinition` | 组件挂在子节点或没挂 | 移到 prefab 根节点 |
| 启动报 `Duplicate cueId` / `empty cueId` | cueId 重复或为空 | 改成唯一非空值 |
| 特效完全不可见 | 通道 Sorting Layer/Order 被遮挡、相机裁剪、材质贴图问题 | 先在 prefab 上单独预览，再检查通道偏移 |
| 染色不生效 | 未声明 `Tint`、未传 tint、Gradient Mode 不是 Color/TwoColors | 勾选能力、传 `WithTint` 或 `color` 参数、调整 Gradient Mode |
| 参数覆盖不生效 | 未声明、类型不一致、实例里没有 Sink | 补声明、改类型、在对应粒子系统上挂 Sink |
| `Sustained` 一直不停 | 无自动结束逻辑 | 保存句柄 `Stop()` 或 `StopAll()` |
| 切场景后特效残留 | 运行时 DontDestroyOnLoad | 切场景时按需 `StopAll()` |
| 同屏同类特效变少/消失 | `maxConcurrent` + 优先级抢占 | 调大上限或调整优先级，查看 `Stats.preempted` |
| 正式包看不到警告 | 告警仅编辑器/开发包输出 | 用编辑器或 Development Build 排查 |

---

## 五、关键约束速查

1. 数据唯一来源是 prefab 根节点的 `VfxEffectDefinition`；Catalog 只是索引与规则表。
2. `cueId` 是运行时主键：唯一、稳定、区分大小写。
3. 能力（Intensity/Tint/Opacity/TimeScale）必须显式声明才可被请求覆盖。
4. 参数必须"声明 + Sink"同时具备才会生效；Float 会被 Clamp。
5. 时长只由定义决定，`Sustained` 必须手动停止。
6. 通道只影响挂载根节点与 sortingOrder 偏移，不影响 Sorting Layer。
7. 运行时跨场景常驻；场景切换按需 `StopAll()`。
8. 告警仅开发期输出且按 key 去重；排查问题时看编辑器或 Development Build 控制台。
