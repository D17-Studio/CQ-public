# 轨道移动系统

## 概述

轨道移动系统让谱师可以自由设定每条轨道的位置、旋转、不透明度随时间变化的动画。三个核心脚本协同工作：

```
LaneMotionEffect         LaneKeyframe         LaneState
   (数据结构)       →     (关键帧)      →   (状态查询)
   谱师设定               时间-值曲线         每帧查询当前值
```

---

## 文件职责

| 文件 | 说明 |
|------|------|
| `LaneMotionEffect.cs` | 纯数据结构，谱师设定一条轨道变化（位置/旋转/不透明度） |
| `LaneKeyframe.cs`     | 曲线和关键帧，实现求值逻辑和 PreCurve 回退链 |
| `LaneState.cs`        | 状态管理器，接收 LaneMotionEffect 列表，提供时间查询 API |

---

## LaneMotionEffect

谱师设定的一条轨道变化。一条 LaneMotion 可以同时包含多个动画组件（Position + Rotation + Opacity 在同一时间段）。

```csharp
public struct LaneMotionEffect
{
    public int StartTime;               // 开始时间（ms）
    public int LaneIndex;               // 轨道序号（-3 ~ 3）
    public int Duration;                // 持续时间（ms）
    public Vec2 Anchor;                 // 锚点坐标（旋转中心）
    public PositionMotion? Position;    // 位置动画
    public RotationMotion? Rotation;    // 旋转动画
    public OpacityMotion? Opacity;      // 不透明度动画
}
```

### PositionMotion

```csharp
public struct PositionMotion
{
    public bool UseRelative;    // 相对模式：Pos 为偏移量；绝对模式：Pos 为世界坐标
    public Vec2 Pos;            // 目标位置 / 偏移量
    public EaseCurve CurveMode; // 缓动曲线
}
```

### RotationMotion

```csharp
public struct RotationMotion
{
    public bool UseRelative;    // 相对模式：Angle 为增量；绝对模式：Angle 为最终角度
    public float Angle;         // 角度（度）
    public EaseCurve CurveMode;
}
```

### OpacityMotion

```csharp
public struct OpacityMotion
{
    public bool AffectHead;     // 影响轨道头
    public bool AffectLine;     // 影响轨道线
    public bool AffectKey;      // 影响轨道按键
    public bool AffectNote;     // 影响轨道上的 Note
    public float Opacity;       // 目标不透明度（0~1）
    public EaseCurve CurveMode;
}
```

Opacity 可以只影响部分元素——比如只让轨道线半透明而头不变。四个 Affect 标志独立控制。

---

## LaneKeyframe

由 `LaneMotionEffect` 转换而来，存储一个时间窗口内的所有曲线。通过 **PreCurve 回退链** 实现属性继承。

### Curve（单值曲线）

```csharp
public struct Curve
{
    public int StartTime;
    public int Duration;
    public float StartValue;
    public float EndValue;
    public EaseCurve CurveMode;
    public bool PersistEndValue;   // true：有效期持续到下一帧；false：结束后不维持
}
```

| 方法 | 说明 |
|------|------|
| `Evaluate(int time)` | 在曲线时间段内插值并应用缓动 |
| `IsInEffect(int time)` | 以 `PersistEndValue` 为依据判断是否仍生效 |

### LaneKeyframe

```csharp
public class LaneKeyframe
{
    public LaneKeyframe PreCurve;       // 回退链：上一个关键帧
    public int StartTime;
    public int Duration;
    public Vec2 Anchor;
    public (Curve X, Curve Y)? PositionCurve;
    public Curve? RotationCurve;
    public Curve? HOpacityCurve;        // 轨道头不透明度
    public Curve? LOpacityCurve;        // 轨道线不透明度
    public Curve? KOpacityCurve;        // 按键不透明度
    public Curve? NOpacityCurve;        // Note 不透明度
}
```

**回退链机制**：每个属性查询方法（`Position(time)`、`Rotation(time)` 等）先检查自身是否持有该曲线，如果没有或已失效，则递归回退到 `PreCurve`，直到找到有效曲线或返回默认值。

**Anchor + Rotation 耦合**：Position 的计算需要知道当前 Rotation 才能正确应用锚点旋转后坐标，因此它们不能分开存储为独立 List，必须在同一个 LaneKeyframe 内。

---

## LaneState

管理七个轨道（-3 ~ 3）的状态，接收 `List<LaneMotionEffect>` 并在构造时全部转为 `LaneKeyframe` 列表。

### 构造

```csharp
var laneState = new LaneState(chart.GetLaneMotions());
```

内部调用 `AddCurves` 逐条处理：从已有曲线取 StartValue、计算 PersistEndValue、创建新 LaneKeyframe 并链接 PreCurve。

### 查询 API

```csharp
Vec2  Position(laneIndex, time)     // 轨道位置
float Rotation(laneIndex, time)     // 轨道旋转（度）
float HeadOpacity(laneIndex, time)  // 轨道头不透明度（0~1）
float LineOpacity(laneIndex, time)  // 轨道线不透明度
float KeyOpacity(laneIndex, time)   // 按键不透明度
float NoteOpacity(laneIndex, time)  // Note 不透明度
```

每个方法通过二分查找（实际为反向线性扫描，曲线数量少时足够）定位最后一个 `StartTime <= time` 的 LaneKeyframe 并求值。

---

## 谱面文本表示

```
LaneMotion - Time:1000 ; Lane:0 ; Duration:500 ; Anchor:(0,0) ; Position:[Absolute,(200,0),Linear] ; Rotation:[Relative,90,InQuad] ; Opacity:[Affect,(Head,Line,Key,Note),0.5,OutCubic]
```

Position / Rotation / Opacity 均为可选，没写即不执行该动画。
