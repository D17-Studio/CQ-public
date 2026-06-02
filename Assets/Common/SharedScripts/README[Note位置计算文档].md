# NotePositionCalculator

## 概述

纯数据计算类，不依赖 Unity API（可被 .NET 制谱器复用）。根据 Note 数据、当前时间和流速，计算 Note 在屏幕上的世界坐标，支持轨道旋转跟随。

---

## 核心公式

每个点先计算"起始位置"（PointTime = TrackTime 时的位置），再沿轨道旋转方向偏移：

```
lanePos   = LaneState.Position(LaneIndex, TrackTime)
startPos  = lanePos + Pos（相对模式）/ Pos（绝对模式）
distance  = (PointTime - TrackTime) / 1000 × Speed
offset    = Vec2(0, distance).Rotate(LaneState.Rotation(LaneIndex, TrackTime))
pointPos  = startPos + offset
```

---

## 流速计算

| SpeedMode | 公式 |
|-----------|------|
| `Default` | `GlobalSpeed` |
| `Multiplier` | `GlobalSpeed × SpeedValue` |
| `Fixed` | `SpeedValue` |

---

## 各类型 Note 输出

### Dot

```
outPoints[0] = 头部位置（过判定点后继续下落）
```

### Dash

```
outPoints[0] = 头部
outPoints[1] = Tune1起点
outPoints[2] = Tune1终点
...
跟随点 = 所有超时的点收缩到当前弧段的起点
```

- 每个点独立下落，不 clamp，线段只平移不形变
- 超时点跟随当前弧段移动（`GetFollowPos`），渲染时两两连线即可
- Tune 起点 = Tune 正解时间，终点 = 起点 + Tune.Duration
- X 偏移：相对模式 = 目标轨道 X + PosValue，绝对模式 = PosValue

### Mute

```
outPoints[0] = 头部
outPoints[1] = 尾部
```

与 Dash 行为一致，不含 Tune 点。

---

## TuneData

| 字段 | 说明 |
|------|------|
| `TargetTime` | 正解时间（ms） |
| `Direction` | 方向 Left/Right |
| `UseRelative` | 是否相对位置 |
| `TargetLane` | 相对模式目标轨道 |
| `PosValue` | 偏移量 Vec2（相对）/ 世界坐标 Vec2（绝对） |
| `Duration` | 过渡持续时间（ms），默认 0 |

---

## 使用方法

```csharp
var calculator = new NotePositionCalculator(globalSpeed: 1080f, laneTransform);

List<Vec2> buffer = new();
calculator.Calculate(noteData, trackTimeMs, buffer);

foreach (var point in buffer)
    Draw(point);
```

`buffer` 由调用方持有，每帧 Clear 后复用，零 GC 开销。

---

## 相关文件

- `NotePositionCalculator.cs` — 本类
- `LaneState.cs` — 轨道位置/旋转查询
- `Vec2.cs` — 二维向量（含 Rotate）
- `NoteData.cs` — Note 数据结构（含 `TuneData`）
- `../Features/GamePlay/Scripts/NoteJudge/README[判定系统文档].md` — 判定系统
