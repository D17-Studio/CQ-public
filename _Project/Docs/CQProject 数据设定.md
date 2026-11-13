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

## 文件与解析

- 谱面文件为 `.txt`（Unity TextAsset），内容为 JSON；扩展名与内容格式解耦。
- 解析入口：`ChartSheet.Parse(string text, Action<string> onWarning = null)`，格式细节由 `ChartSerializer` 负责。
- 序列化：`ChartSerializer.Serialize(ChartSheet chart)`。
- 文件位置：`Assets/Features/SharedScripts/ChartSheet.cs`、`ChartSerializer.cs`。
- 失败语义：
  - 空文本 / 非法 JSON / 未知枚举 / 缺必填字段 / 不支持的版本 → 抛 `ChartException`（含字段路径与行列号）。
  - 缺少 `Version` → 按版本 1 处理并告警。
  - 结构合法但没有任何 Note → 告警并正常加载。
- `ChartSheet` 构造时统一排序：Note 按 `TargetTime`；LaneEvent 按 `StartTime`，同刻按 `Duration` 降序；Zone / Parent 同刻后写覆盖。
- 六类 LaneEvent 的 `Layer` 仅供制谱器时间轴分层，运行时忽略，但随谱面存储。

## 轨道编号

`LaneIndex` / `TargetLane` / `ParentLane` 为视觉轨道序号，约定为**从 0 开始的非负整数**（0、1、2、3…）。该约定不强制校验：解析与运行时对负数、跳号同样可以工作，但不建议在新谱面中使用。

## JSON 总览

```json
{
  "Version": 1,
  "Bpm": [ { "StartTime": 0, "Bpm": 165 } ],
  "Notes": [
    { "Type": "Dot", "LaneIndex": 0, "TargetTime": 2000, "SustainDuration": 0, "AppearOffset": -3000 },
    { "Type": "Dash", "LaneIndex": 3, "TargetTime": 5000, "SustainDuration": 1000, "AppearOffset": -3000,
      "LinkedTunes": [
        { "TargetTime": 5500, "UseRelative": true, "TargetLane": 2,
          "PosValue": { "X": -100, "Y": 0 }, "Duration": 200 } ] },
    { "Type": "Dot", "LaneIndex": 1, "TargetTime": 7000, "AppearOffset": -3000, "Fake": "Default" }
  ],
  "LaneEvents": {
    "Transform": [
      { "LaneIndex": 0, "StartTime": 1000, "Duration": 4000, "Layer": 0,
        "Anchor": { "X": 0, "Y": 0 },
        "XCurve": { "From": -200, "FromMode": "Absolute", "To": -350, "ToMode": "Absolute", "Ease": "InOutSine" },
        "RotationCurve": { "From": 0, "FromMode": "Absolute", "To": 15, "ToMode": "Absolute", "Ease": "InOutSine" } }
    ],
    "Opacity": [
      { "LaneIndex": 0, "StartTime": 0, "Duration": 0, "Layer": 0, "Targets": "Head, Line, Label",
        "ValueCurve": { "From": 1, "FromMode": "Absolute", "To": 1, "ToMode": "Absolute", "Ease": "Linear" } }
    ],
    "Length": [],
    "Speed": [
      { "LaneIndex": 0, "StartTime": 0, "Duration": 0, "Layer": 0, "SpeedMode": "Multiplier",
        "ValueCurve": { "From": 1, "FromMode": "Absolute", "To": 1, "ToMode": "Absolute", "Ease": "Linear" } }
    ],
    "Zone":   [ { "LaneIndex": 0, "StartTime": 0, "Layer": 0, "TargetZone": "L1" } ],
    "Parent": [ { "LaneIndex": 2, "StartTime": 0, "Layer": 0, "ParentLane": 0 } ]
  }
}
```

格式规则：

- 枚举一律写字符串；flags（`Targets`）用逗号分隔，如 `"Head, Line, Label, Note"`；写数字枚举视为错误。
- 可空字段在写档时省略，读取时缺失等价于 null：`Fake`、`LinkedTunes`、`XCurve`、`YCurve`、`RotationCurve`、`ParentLane`。
- `Vec2` 统一写作 `{ "X": …, "Y": … }`。
- 缺失的 `LaneEvents` 子列表视为空列表；未知字段忽略（前向兼容）。
- 数值使用 invariant culture。

---

## Note

### NoteData

- **Type** 音符类型：Dot / Dash / Mute（必填）
- **LaneIndex** 视觉轨道序号（必填；约定从 0 开始的非负整数，不强制校验）
- **TargetTime** 正解时间（ms，必填）
- **SustainDuration** 长条持续时间（ms），默认 0
- **AppearOffset** 视觉提前出现量（ms），默认 -3000
- **Fake** Fake 模式：`"Default"` 或省略（省略 = 普通 Note）
- **LinkedTunes** 附属 Tune 列表（仅 Dash 有效）
- **JudgeZone** 运行时派生值：加载时按 `TargetTime` 从轨道 Zone 事件采样并固化；不属于谱面内容，不参与序列化

```c#
public struct NoteData
{
    public NoteType Type;
    public int LaneIndex;
    public int TargetTime;
    public int SustainDuration;
    public int AppearOffset;
    public FakeMode? Fake;
    public TuneData[] LinkedTunes;
    [NonSerialized] public JudgeZone JudgeZone;   // 运行时派生，不序列化
}
```

Fake Note 不参与判定与计分，仅作为视觉噪声加载；到 `TargetTime` 的表现由 View 层负责。

### TuneData

附属于 Dash，每个 Tune 代表 Dash 长条上的一次水平位移；仅作为 Dash 身子折线 Mesh 的几何数据，不参与判定、不计分、没有独立视觉。

- **TargetTime** 正解时间（ms，必填）
- **UseRelative** 相对位置模式，默认 false
- **TargetLane** 目标轨道（必填；相对模式下使用；约定从 0 开始，不强制校验）
- **PosValue** 偏移量（相对）/ 世界坐标（绝对），必填，写作 `{ "X": …, "Y": … }`
- **Duration** 到达目标点后的过渡时长（ms），默认 0

```c#
public struct TuneData
{
    public int TargetTime;
    public bool UseRelative;
    public int TargetLane;
    public Vec2 PosValue;
    public int Duration;
}
```

---

## LaneEvent

六类事件分别存于 `LaneEvents.Transform / Opacity / Length / Speed / Zone / Parent` 数组。

公共字段：

| 字段 | 说明 |
|------|------|
| `LaneIndex` | 视觉轨道序号（必填；约定从 0 开始，不强制校验） |
| `StartTime` | 开始时间（ms，必填） |
| `Duration` | 持续时间（ms），默认 0 |
| `Layer` | 制谱器时间轴分层，运行时忽略，默认 0 |

### ChannelCurve

曲线数据，五个字段全部必填：

| 字段 | 说明 |
|------|------|
| `From` | 起始值 |
| `FromMode` | `Absolute` / `Relative` |
| `To` | 结束值 |
| `ToMode` | `Absolute` / `Relative`（相对起点增量） |
| `Ease` | 缓动类型，见 `Easing.cs` |

```c#
public struct ChannelCurve
{
    public float From;
    public ValueMode FromMode;
    public float To;
    public ValueMode ToMode;
    public EaseCurve Ease;
}
```

### Transform

| 字段 | 说明 |
|------|------|
| `Anchor` | 旋转锚点，省略为 `(0,0)`；存在时 `X` / `Y` 必填 |
| `XCurve` | X 位移曲线，可省略 |
| `YCurve` | Y 位移曲线，可省略 |
| `RotationCurve` | 旋转曲线（度），可省略 |

三条曲线可各自为空；为空表示该通道不产生新动画。

### Opacity

| 字段 | 说明 |
|------|------|
| `Targets` | `Head` / `Line` / `Label` / `Note` 的 flags，逗号分隔（必填） |
| `ValueCurve` | 不透明度曲线（必填） |

### Length

| 字段 | 说明 |
|------|------|
| `ValueCurve` | 轨道线视觉长度倍率曲线（必填）；1 = prefab 原始长度，允许 0 / 负值 |

### Speed

| 字段 | 说明 |
|------|------|
| `SpeedMode` | `Multiplier`（玩家流速 × 曲线值，默认）/ `Fixed`（忽略玩家流速） |
| `ValueCurve` | 流速曲线（必填） |

运行时按 Note 头部轨道的 `ValueCurve` 对 `[当前时间, 该点正解时间]` 做积分换算下落距离；`Multiplier` / `Fixed` 按事件区间各自生效，允许 0 与负值。

### Zone

| 字段 | 说明 |
|------|------|
| `TargetZone` | `All` / `Left` / `Right` / `L1` / `L2` / `R1` / `R2`（必填） |

瞬时事件。运行时在加载期按 Note 的 `TargetTime` 采样并固化到 `NoteData.JudgeZone`，判定全程使用该值。

### Parent

| 字段 | 说明 |
|------|------|
| `ParentLane` | 父轨道序号；省略 = 无父（约定从 0 开始，不强制校验） |

瞬时事件。运行时在 `GetFrame` 查询时递归合成父级 Position / Rotation，不继承 Speed / Length / Opacity / Zone。

---

## BPM

- **StartTime** 开始时间（ms，必填）
- **Bpm** BPM 值（必填）

```c#
public struct BpmData
{
    public int StartTime;
    public float Bpm;
}
```

文本表示：

```json
"Bpm": [
  { "StartTime": 0, "Bpm": 170 },
  { "StartTime": 30000, "Bpm": 180 }
]
```

---

## 统计口径

- `DotCount` / `DashCount` / `MuteCount` / `TotalCount` 均不含 Fake Note（供计分使用）。
- Tune 是效果数据，不计入任何统计。
- `LaneIndexList` 汇总 Note、Tune 目标轨道、六类事件与 ParentLane（含 Fake Note 所在轨道）。

---

## 缓动曲线

> 详情请参考 [Easing.cs](../../Assets/Features/SharedScripts/Easing.cs)

---

# Note 位置计算

> 详情请参考 [NotePositionCalculator.cs](../../Assets/Features/SharedScripts/NotePositionCalculator.cs)

根据 Note 数据、当前时间和轨道流速计算屏幕坐标，支持父轨道合成与 Dash 折线渲染。

- [Note 位置计算文档](../../Assets/Features/SharedScripts/README[Note位置计算文档].md)
- 运行时采样与事件语义：[谱面新结构适配计划](../Design/谱面重构/适配计划.md)

---

# 自制谱包（TrackInfo.txt）

> 运行时装载：`CustomTrackLoader`；根目录 `<exe 同级>/CustomTracks/`（编辑器 = 工程根目录），不入库。

## 目录结构

```
CustomTracks/
└─ <pack>/
   ├─ TrackInfo.txt
   ├─ charts/
   │  ├─ ST-*.txt
   │  ├─ HD-*.txt
   │  └─ EX-*.txt
   ├─ <music>.wav
   └─ <cover>.png
```

## TrackInfo.txt

JSON，字段与 `CQMusicGame.Shared.TrackInfo` 一致，由 `TrackInfoSerializer` 解析：

```json
{
  "Version": 1,
  "MusicTitle": "曲名",
  "MusicArtist": "曲师",
  "MusicAlbum": "专辑",
  "MusicCoverArtist": "曲绘画师",
  "MusicCoverPath": "cover.png",
  "MusicPath": "song.wav",
  "MusicStartOffset": 840,
  "PreviewStart": 0,
  "PreviewEnd": 0,
  "Charts": [
    { "DifficultyId": 1, "ChartConstant": 5, "ChartPath": "charts/ST.txt", "ChartArtist": "谱师" }
  ]
}
```

- `DifficultyId`：0/1/2/3 = EZ/ST/HD/EX；越界跳过，重复难度保留第一个。
- `MusicStartOffset`：正 = 开头补静音，负 = 裁掉开头；正偏移时打歌时钟先走完静音段，再按首次 `GetSourcePlayPosition` 校准无缝交给音频时钟；预览按文件时间，不含该空档。
- `PreviewStart/End`：预览区间（毫秒）；0/0 = 默认 15000ms 窗口。
- 成绩键 = `MusicTitle.Trim() + "_" + 难度代号 + "_" + 存档签名`：签名为 track 级 `TrackData.ArchiveSignature`，官谱来自 SO `OfficialTrackInfo.ArchiveSignature`（手填、可空）；签名为空保留末尾 `_`。自制谱签名留空（键形如 `曲名_Dif0_`，同名自制曲共用键，已接受）。
- `MusicPath/CoverPath/ChartPath` 必须是包内相对路径；非 PCM WAV、缺 WAV、缺谱面均跳过对应包 / 难度；缺封面只降级为 null。

## 运行时模型

`OfficialTrackAdapter`（官谱 SO）与 `CustomTrackLoader`（自制谱）都转换为 `TrackData` / `ChartData`；菜单、`PlaySessionService`、打歌只消费这套标准模型。
