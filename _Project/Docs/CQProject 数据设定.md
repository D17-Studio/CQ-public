CQProject 数据设定

---

# 位置数据

### 屏幕位置与坐标：

**16 : 9 的屏幕大小为1920×1080，中心为坐标轴原点**

- 也就是说，左右 -960 ~ 960 ， 上下 -540 ~ 540 在屏幕范围内

### 流速：

**流速代表Note在一秒内可以移动多少单位长度**

- 1080流速的Note可以在一秒内刚好从屏幕顶端移动到屏幕底端

---

# 时间

**时间使用毫秒（ms）**

---

# 谱面

## Note：

> 详情请参考 [NoteData.cs](../../Assets/Common/SharedScripts/NoteData.cs)

**NoteData 结构体：**

- **TargetTime** 正解时间（ms）
- **LaneIndex** 轨道序号（-3 ~ 3）
- **Type** 音符类型：Dot / Dash / Mute
- **SustainDuration** 长条持续时间（ms）
- **AppearOffset** 视觉提前出现量（ms），默认 -3000（正解前3秒出现）
- **SpeedMode** 流速模式：Default / Multiplier / Fixed
- **SpeedValue** 流速值
- **LinkedTunes** 附属 Tune 列表（仅 Dash 有效）

```c#
public struct NoteData
{
    public NoteType Type;
    public int LaneIndex;
    public int TargetTime;
    public int SustainDuration;
    public int AppearOffset;
    public NoteSpeedMode SpeedMode;
    public float SpeedValue;
    public TuneData[] LinkedTunes;
}
```

---

### TuneData

附属于 Dash，每个 Tune 代表 Dash 长条上的一次水平位移。

| 字段 | 说明 |
|------|------|
| `TargetTime` | 正解时间（ms） |
| `Direction` | 方向：Left / Right |
| `UseRelative` | 是否相对位置 |
| `TargetLane` | 相对模式目标轨道（-3 ~ 3） |
| `PosValue` | 偏移量 Vec2（相对）/ 世界坐标 Vec2（绝对） |
| `Duration` | 过渡持续时间（ms），默认 0 |

```c#
public struct TuneData
{
    public int TargetTime;
    public TuneDirection Direction;
    public bool UseRelative;
    public int TargetLane;
    public Vec2 PosValue;
    public int Duration;
}
```

---

### Note 文本存储

`音符类型` - Time:`正解时间` ; Lane:`轨道序号` ; Duration:`持续时间` ; Appear:`提前出现量` ; Speed:`流速模式`(`流速值`)

Dash 的附属 Tune 以缩进 ` - Tune -` 表示。

> 分隔符（`-` `:` `;`）前后的空格不影响解析。

```
Dot - Time:10231 ; Lane:-2 ; Duration:0 ; Appear:-3000 ; Speed:Default()
Mute - Time:95130 ; Lane:0 ; Duration:0 ; Appear:-3000 ; Speed:Fixed(652.234)
Dash - Time:2023 ; Lane:1 ; Duration:1250 ; Appear:-3000 ; Speed:Multiplier(1.25647)
 - Tune - Time:2134 ; Direction:Left ; Motion:Relative(-1,(-200,0)) ; Duration:200
 - Tune - Time:2245 ; Direction:Right ; Motion:Absolute((135,0)) ; Duration:100
```

---

### BPM 变化点

曲中变速数据，按 StartTime 升序排列。

> 详情请参考 [BpmPoint.cs](../../Assets/Common/SharedScripts/BpmPoint.cs)

```c#
public struct BpmPoint
{
    public int StartTime;   // 开始时间（ms）
    public float Bpm;       // BPM 值
}
```

文本存储：

```
BPM - Time:0 ; BPM:170
BPM - Time:30000 ; BPM:180
```

---

## 轨道移动效果：

> 详情请参考 [LaneMotionEffect.cs](../../Assets/Common/SharedScripts/LaneMotionEffect.cs)
> 曲线实现：[LaneKeyframe.cs](../../Assets/Common/SharedScripts/LaneKeyframe.cs)
> 状态查询：[LaneState.cs](../../Assets/Common/SharedScripts/LaneState.cs)

**Vec2 结构体：**

> 位于独立文件 [Vec2.cs](../../Assets/Common/SharedScripts/Vec2.cs)

```C#
public struct Vec2
{
    public float x;
    public float y;

    public Vec2 Rotate(float degrees);  // 绕原点逆时针旋转
    public static readonly Vec2 Zero;
}
```

**轨道移动结构体：**

```c#
public struct LaneMotionEffect
{
    public int StartTime;                    // 开始时间（毫秒）
    public int LaneIndex;                    // 轨道序号
    public int Duration;                     // 持续时间（毫秒）
    public Vec2 Anchor;                      // 锚点坐标（旋转中心）

    // 三个可选的动画组件（null 表示不执行该动画）
    public PositionMotion? Position;
    public RotationMotion? Rotation;
    public OpacityMotion? Opacity;
}
```

**位置运动结构体：**

```C#
public struct PositionMotion
{
    public bool UseRelative;    // 是否使用相对位置
    public Vec2 Pos;            // 变化后位置 / 偏移量
    public EaseCurve CurveMode; // 动画曲线
}
```

**旋转变化结构体：**

```C#
public struct RotationMotion
{
    public bool UseRelative;   // 是否使用相对旋转量
    public float Angle;        // 角度（度）
    public EaseCurve CurveMode;// 动画曲线
}
```

**不透明度变化结构体：**

```C#
public struct OpacityMotion
{
    public bool AffectHead;    // 是否影响轨道头
    public bool AffectLine;    // 是否影响轨道线
    public bool AffectKey;     // 是否影响轨道按键
    public bool AffectNote;    // 是否影响轨道上的 Note
    public float Opacity;      // 不透明度（0~1）
    public EaseCurve CurveMode;// 动画曲线
}
```

**文件表示：**

LaneMotion - Time:`正解时间` ; Lane:`轨道序号` ; Duration:`持续时间` ; Anchor:`锚点位置` ; Position:`位置运动` ; Rotation:`旋转` ; Opacity:`不透明度变化`

> 分隔符（`-` `:` `;`）前后的空格不影响解析。

其中：

- Position:`[绝对/相对 , 位置 , 运动曲线]`

- Rotation:`[绝对/相对 , 旋转量 , 运动曲线]`

- Opacity:`[Affect,(应用部位) , 不透明度 , 运动曲线]`

应用部位可选值：Head、Line、Key、Note（可多选，逗号分隔）

比如：

```
LaneMotion - Time:21503 ; Lane:0 ; Duration:2153 ; Anchor:(0,0) ; Position:[Absolute,(200.00,-256.21),OutCubic] ; Rotation:[Relative,90.00,InQuad] ; Opacity:[Affect,(Head,Line,Key,Note),0.235,Linear]
```

- [轨道移动系统文档](../../Assets/Common/SharedScripts/README[轨道移动系统文档].md)

---

## 缓动曲线：

> 详情请参考 [Easing.cs](../../Assets/Common/SharedScripts/Easing.cs)

---


# Note 位置计算

> 详情请参考 [NotePositionCalculator.cs](../../Assets/Common/SharedScripts/NotePositionCalculator.cs)

根据 Note 数据、当前时间和流速计算屏幕坐标，支持轨道旋转跟随。Dash 长条支持 Tune 水平位移后的多段折线渲染。

- [Note 位置计算文档](../../Assets/Common/SharedScripts/README[Note位置计算文档].md)