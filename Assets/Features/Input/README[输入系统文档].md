# Input 输入模块（InputService）

## 概述

输入层唯一入口：设备 → 语义意图的翻译 + 键位重绑 + 覆盖持久化。2026-10-15 重写（背景与定案见《主菜单UI框架设计》决策 19）。

- **形态**：纯 C# `InputService`（无 MonoBehaviour、无单例访问）+ SO 安装器 `InputInstaller` 绑 ProjectContext（仿 `PlayerConfigInstaller`）；旧 `MonoSingletonHungry` / `Bootstrapper` 自拉起路径已摘除（三件套文件保留、零引用——用户定案）。
- **位置**：`Assets/Features/Input/`——`Settings/` = `InputActions.inputactions`（唯一映射源）+ 生成包装类 + `JudgeZoneConfig.asset`（唯一按键/判定区配置）+ `InputInstaller.asset`；`Scripts/` = 本模块全部代码。
- **消费范式：全轮询**——判定热路径需"同一帧同一段代码读按键 + 读曲目时间"，`WasPressedThisFrame` 边沿语义天然对齐帧；事件式会在输入更新阶段触发、与 `TrackController.Update` 时间源错拍。重绑是唯一例外（一次性异步，回调式）。
- **Action 地图**：`GameActions`（4 基础键 L2/L1/R1/R2 + 5 额外键 ExtraLeft×2 / ExtraRight×2 / ExtraAll×1）/ `UIActions`（`Back` + `MenuUp/Down/Left/Right`）；全部常开、不切 map；重绑期间仅 disable 被捕获的动作。
- **物理键位**：`L2 = D（左外侧）`、`L1 = F（左内侧）`、`R1 = J（右内侧）`、`R2 = K（右外侧）`；`JudgeZoneConfig.asset` 的 L1/L2/R1/R2 字段必须按此语义配置。
- **输入分区**：收集侧使用 `InputZone`（L1/L2/R1/R2/ExtraLeft/ExtraRight/ExtraAll），与 `JudgeZone` 的聚合语义分开；同一按键只归属首个命中的输入分区，保证一次点击只产生一条 `ZoneInput`。

## 公开 API

| 成员 | 类型 | 说明 |
|------|------|------|
| `InputService(string filePath, JudgeZoneCatalog zoneCatalog)` | 构造 | 构造即加载覆盖 + 启用输入；`Application.quitting` 兜底落盘 |
| `InputZoneDown/Hold/Up(InputZone zone)` | 方法 | 输入分区三读：区内任一按键按下 / 按住 / 松开 |
| `BackDown()` | 方法 | 返回键（ESC）本帧按下；重绑捕获中 / 取消帧自抑制（见"重绑语义"） |
| `MenuUp()/MenuDown()/MenuLeft()/MenuRight()` | 方法 | 菜单方向键本帧按下（无按住 / 松开语义） |
| `StartRebind(JudgeKeyAction action, onComplete, onCancel)` | 方法 | 启动重绑捕获（回调式） |
| `IsRebinding` | 属性 | 捕获中（重绑 UI 点击入口守卫用；ESC 抑制已内联在 BackDown） |
| `GetKeyName(JudgeKeyAction action)` | 方法 | 绑定按键显示名（按键标签 / 重绑 UI 用） |
| `ResetBinding(JudgeKeyAction action)` | 方法 | 恢复默认绑定并立即落盘 |

## 持久化（customBindings.json）

- 路径 `Application.persistentDataPath/customBindings.json`；格式 = Unity `SaveBindingOverridesAsJson`——**与旧实现同文件同格式，现档兼容**。
- 健壮性对齐 PlayerConfigStore：缺档 = 默认绑定；坏档 → 备份 `.bak` 留证 + 默认绑定继续；原子写（tmp + replace）；写失败保脏、退出兜底重试。
- 键位**独立存储**（决策 6 维持）：不与 `playerConfig.json` 合并。

## 重绑语义

- 捕获期间被捕获动作 disable（防按新键瞬间触发该轨动作）；ESC 取消（原绑定保留、不落盘）；换键成功 → 落盘 + `onComplete`。
- 一次只允许一个捕获（捕获中再调 `StartRebind` 直接忽略）。
- **ESC 吞噬**：捕获中按 ESC 取消重绑的同一帧，取消回调先于消费者轮询执行——`BackDown()` 在"捕获中 + 取消帧"自抑制，防"取消捕获 + 关弹层"双触发（2026-10-16 修复并实机验证；消费者无需特判）。
- **交互实机验证通过（2026-10-16，随设置弹层键位区落地）**：启动 / 完成换键 / ESC 取消 / RESET 全路径；一键多轨（同键绑两轨）为规则内合法、不做冲突检测（定案见决策 20）。

## 消费点

| 消费方 | 用途 |
|--------|------|
| `TrackController` | 区域三读（输入队列 / Hold 判定） |
| `PauseInputRouter` | `BackDown()` → 暂停 / 恢复路由 |
| `LaneViewManager` | `GetKeyName` 按键名标签 |
| `SongSelectPresenter` | `Menu*` 轮询（↑↓ 切歌 / ←→ 切难度；0.08s 节流住 Presenter） |
| `SettingsPresenter` | 键位区：`GetKeyName` 下推 / `StartRebind` 捕获 / `ResetBinding` / `IsRebinding` 守卫 |

（菜单其余输入——点格 / 滚轮 / 按钮——走 UGUI 指针事件，留在 View。）

## 相关文件

- `InputService.cs`（内核）/ `InputInstaller.cs`（装配）/ `KeyNameDisplay.cs`（按键名格式化）
- `../Settings/InputActions.inputactions`（唯一映射源，生成类同目录）/ `../Settings/InputInstaller.asset`（已挂 ProjectContext）
- 设计定案：[主菜单UI框架设计.md](../../../../_Project/Docs/主菜单UI框架设计.md) 决策 19；旧缺陷修复记录见同文档"现有代码问题 1"
