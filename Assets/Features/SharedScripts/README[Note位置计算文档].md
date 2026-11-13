# NotePositionCalculator

## 概述

纯数据计算类，不依赖 Unity API（可被 .NET 制谱器复用）。根据 Note 数据、当前时间和流速，计算 Note 在屏幕上的世界坐标，支持轨道旋转跟随。

---

## 核心公式

每个点先计算"起始位置"（PointTime = TrackTime 时的位置），再沿轨道旋转方向偏移：

```
frame     = LaneState.GetFrame(点的 LaneIndex, TrackTime)
startPos  = frame.Position + Pos（相对模式）/ Pos（绝对模式）
distance  = ∫[TrackTime → PointTime] Speed(Note头部Lane)(t) dt
offset    = Vec2(0, distance).Rotate(frame.Rotation)
pointPos  = startPos + offset
```

- 位置基准/旋转取**该点自身**的 `LaneIndex`；流速曲线取 **Note 头部**的 `LaneIndex`，所有点共用同一条曲线。
- 每个点的积分上限是该点自己的 `PointTime`（Dash 头、Tune 起点/终点、尾点各不相同）。
- 无 Speed 事件时曲线恒为 1，结果与 `GlobalSpeed × 毫秒差 / 1000` 完全一致。

---

## 流速计算

| SpeedMode | 有效流速 |
|-----------|----------|
| `Multiplier` | `GlobalSpeed × 曲线值` |
| `Fixed` | `曲线值` |
| 无事件 | `GlobalSpeed` |

- 流速曲线可随时间变化，距离用 `Easing.Integral` 对曲线做**解析积分**；同一区间内 Multiplier/Fixed 各自按所属事件生效。
- Speed 允许 0 / 负值，不钳制。
- 查询入口：`LaneState.IntegrateSpeed(laneIndex, fromTime, toTime, globalSpeed)`，返回世界单位距离。

---

## 各类型 Note 输出

### Dot

```
outPoints[0] = 头部位置（过判定点后继续积分，距离为负）
```

### Dash

```
outPoints[0] = 头部
outPoints[1] = Tune1起点
outPoints[2] = Tune1终点
...
跟随点 = 所有超时的点收缩到当前弧段的起点
```

- 每个点独立下落，不 clamp；各点用自身目标时间作为积分上限
- 所有点（Tune 起点/终点/尾点）的流速统一取 Note 头部 Lane 的曲线
- 超时点跟随当前弧段移动（`GetFollowPos`），渲染时两两连线即可
- Tune 起点 = Tune 正解时间，终点 = 起点 + Tune.Duration
- X 偏移：相对模式 = 目标轨道 X + PosValue，绝对模式 = PosValue

### Mute

```
outPoints[0] = 头部
outPoints[1] = 尾部（TargetTime + SustainDuration）
```

与 Dash 行为一致，不含 Tune 点。

- shared 计算层不做最小视觉长度处理；如需保证可见长度，由游戏侧视觉层实现（当前未实现）

---

## TuneData（Dash 折线控制点）

Tune 仅用于描述 Dash 身子折线的转折点，不参与判定/计分，也没有独立视觉。

| 字段 | 说明 |
|------|------|
| `TargetTime` | 正解时间（ms） |
| `UseRelative` | 是否相对位置 |
| `TargetLane` | 相对模式目标轨道 |
| `PosValue` | 偏移量 Vec2（相对）/ 世界坐标 Vec2（绝对） |
| `Duration` | 过渡持续时间（ms），默认 0 |

---

## 使用方法

```csharp
var calculator = new NotePositionCalculator(globalSpeed: 1080f, laneState);

List<Vec2> buffer = new();
calculator.Calculate(noteData, trackTimeMs, buffer);

foreach (var point in buffer)
    Draw(point);
```

`buffer` 由调用方持有，每帧 Clear 后复用，零 GC 开销。

---

## 相关文件

- `NotePositionCalculator.cs` — 本类
- `LaneState.cs` — 轨道位置/旋转查询、流速采样与 `IntegrateSpeed` 积分
- `Easing.cs` — 缓动曲线及其解析积分 `Easing.Integral`
- `Vec2.cs` — 二维向量（含 Rotate）
- `NoteData.cs` — Note 数据结构（含 `TuneData`）
- `../Features/GamePlay/Scripts/NoteJudge/README[判定系统文档].md` — 判定系统
