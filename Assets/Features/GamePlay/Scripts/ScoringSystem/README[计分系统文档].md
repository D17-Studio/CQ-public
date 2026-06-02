# ScoringSystem 计分系统

## 概述

`ScoringSystem` 负责接收每条判定记录，统计分数、Combo 和评价。

---

## 核心概念

### 权重

每种 Note 和每种判定结果各有权重（×100 的整数），分子贡献 = `NoteWeight × ResultWeight`：

|         | Dot | Dash | Mute | Tune |
|---------|-----|------|------|------|
| Weight  | 100 | 100  | 50   | 100  |

|              | Perfect | Early | Late | Bad | Miss | Unknown    |
|--------------|---------|-------|------|-----|------|------------|
| Weight       | 100     | 80    | 80   | 0   | 0    | -1,000,000 |

Mute 半权重（50×100=5000 vs 100×100=10000），其余全权重。
Unknown 权重为极大的负数，一旦出现分数直接崩坏，用于检测程序错误。

### 分母

初始化时计算：所有 Note 均以 Perfect 结算时的分子之和。公式：

```
分母 = Σ(NoteWeight × 100) 对每个Note（含Tune）
```

### 分数

```
分数 = 满分 × 分子 / 分母（四舍五入）
满分 = 1,000,000
```

全程整数运算，无浮点精度损失。

### 评级

| 分数 | 评级 |
|------|------|
| 1,000,000 | P |
| ≥990,000 | S |
| ≥970,000 | A |
| ≥950,000 | B |
| ≥930,000 | C |
| ≥900,000 | D |
| ≥800,000 | E |
| ≥0 | F |

### Combo

- Perfect / Early / Late → Combo +1
- Bad / Miss → Combo 清零
- Unknown → 不变

---

## 数据结构

### 判定计数

`int[4, 6]` 二维数组：
- 行：Dot(0) / Dash(1) / Mute(2) / Tune(3)
- 列：Perfect(0) / Early(1) / Late(2) / Bad(3) / Miss(4) / Unknown(5)

### 偏移记录

四个独立 `List<int>`：`_dotOffsets / _dashOffsets / _muteOffsets / _tuneOffsets`

---

## 公开 API

| 成员 | 类型 | 说明 |
|------|------|------|
| `ScoringSystem(ChartSheet)` | 构造 | 传入谱面，计算分母 |
| `AddRecord(JudgeRecord, NoteType)` | 方法 | 提交一条判定记录 |
| `GetCount(JudgeResult)` | 方法 | 查询所有 Note 的某判定总数量 |
| `GetEvaluate()` | 方法 | 返回评级字符串 |
| `Score` | 属性 | 当前分数 |
| `ComboCount` | 属性 | 当前 Combo |
| `MaxCombo` | 属性 | 最高 Combo |
| `IsAllPerfect` | 属性 | 是否 AP |
| `IsFullCombo` | 属性 | 是否 FC |
| `NoteWeights` | 字典 | Note 权重配置 |
| `ResultWeights` | 字典 | 判定权重配置 |
| `Ranks` | 数组 | 评级阈值配置 |
| `FullMarks` | 常量 | 满分（1,000,000） |

---

## 使用方式

```csharp
// 初始化
var chart = new ChartSheet(chartText);
var scoring = new ScoringSystem(chart);

// 每条判定提交一次
scoring.AddRecord(new JudgeRecord(JudgeResult.Perfect, 15), NoteType.Dot);

// 结束时查询
int score = scoring.Score;
string rank = scoring.GetEvaluate();
int perfects = scoring.GetCount(JudgeResult.Perfect);
```

---

## 相关文件

- `JudgeRecord.cs` — 判定记录结构体
- `../NoteJudge/Note.cs` — Note 基类
- `../../../../Common/SharedScripts/NoteData.cs` — Note 数据结构
- `../../../../Common/SharedScripts/ChartSheet.cs` — 谱面数据集合
