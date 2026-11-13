# NoteJudge 判定系统

## 概述

NoteJudge 负责管理所有 Note 的判定。核心由两个类协作完成：

- **Note**（及子类）——单个 Note 的判定逻辑
- **NoteJudge** ——管理三个判定池，分发输入

### **关键思路：**

将不同的判定行为拆分、归类，构建不同的**“判定池”**进行管理

---

## Note 类型

> Tune 不是独立的 Note 类型：它只是 Dash 折线的几何数据，不参与判定。

### Dot（点击）

- 判定窗口：`[-BadOffset, +GoodOffset]`，默认 [-150ms, +100ms]
- 击中 → 根据偏移量计算 Perfect/Early/Late/Bad → 立即提交分数
- 超时 → Miss

### Dash（长按）

- 判定窗口：`[-GoodOffset, +GoodOffset]`，默认 [-100ms, +100ms]
- 头部击中 → 计算判定结果并保存，**不提交**，进入长按池
- 长按到结束 → 提交之前保存的结果
- 中途松开 → 覆盖为 Miss 并提交

### Mute（自动长按）

- 判定窗口：`[0, +GoodOffset]`，默认 [0, +100ms]
- 不需要点击：按住按键时，到达正解时间的 Mute 自动判定为 **Perfect**
- 其余行为与 Dash 一致（长按到结束或松开）

---

## 三个判定池

| 池 | 存放 | 判定方式 |
|----|------|----------|
| `_dotDashHeadPool` | Dot、Dash | 点击，一次只判一个 |
| `_muteHeadPool` | Mute | 宽松判定（按住自动判） |
| `_sustainPool` | Dash、Mute（头部判定后） | 检测松开和长按结束 |

---

## 完整判定流程（每帧）

```
1. CalcDeltaTime → 计算帧间时间差

2. UpdateMuteTimer
   - 遍历 Hold 输入 → 刷新对应输入分区的计时器（= GoodOffset）
   - 所有计时器递减 deltaTime
   
3. UpdateMuteHeadPool
   - 遍历每个输入分区 → 计时器 > 0 的分区
   - 从 muteHeadPool 中找 CanBeHit 的 Mute → 自动击中 → 移入 sustainPool
   
4. 点击判定
   - TryHitDotDash：遍历 Press 输入 → 命中第一个 CanBeHit 的 Dot/Dash 后立即 break（一次点击只判一个）→ Dash 额外移入 sustainPool
   
5. UpdateSustainPool
   - 长按结束（IsSustainEnded）→ SubmitFinal → 移除
   - 未按住（!IsZoneHeld：没有任何可命中该 Note.JudgeZone 的输入分区按住）→ HandleMiss → 移除
   
6. CheckMissAll
   - 两个头部池各自从头扫描：池按 TargetTime 升序，遇到第一个未 Miss 即停
   - Miss 前缀批量 RemoveRange，再统一 HandleMiss（复用缓冲，无每帧分配）
```

---

## 关键类

### Note（抽象基类）

| 成员 | 说明 |
|------|------|
| `CanBeHit(input, time)` | 抽象，子类定义判定窗口 |
| `OnHit(time)` | 抽象，子类定义击中行为 |
| `IsMissed(time)` | 基类实现：`time - Target > GoodOffset` |
| `IsSustainEnded(time)` | 基类实现 |
| `OnMiss()` | 基类实现：Set Miss → Submit |
| `SubmitFinal()` | 基类实现：Submit 已保存的结果 |
| `CalcJudgeResult(time)` | protected，计算判定结果 + 偏移 |
| `SubmitResult()` | protected，提交到 ScoringSystem |

### NoteJudge

| 成员 | 说明 |
|------|------|
| `AddNote(NoteData)` | 创建 Note 并分配到对应池 |
| `NotesUpdate(inputs, time)` | 常规帧更新 |
| `AutoPlayUpdate(time)` | AutoPlay 帧更新 |

---

## 数据流

```
谱面文本 → 序列化适配器 → NoteData / 事件列表
  → ChartSheet（采样 NoteData.JudgeZone）→ ScoringSystem
  → NoteJudge.AddNote → 创建 Note → 分配池

游戏循环：
  音频时间 → TrackController 收集 ZoneInput → NoteJudge.NotesUpdate(inputs, time)
    → 各池判定 → Note.OnHit / Note.OnMiss / Note.SubmitFinal
      → Note.SubmitResult → ScoringSystem.AddRecord
```

---

## 相关文件

| 文件 | 说明 |
|------|------|
| `Note.cs` | Note 抽象基类 |
| `DotNote.cs / DashNote.cs / MuteNote.cs` | 三样子类 |
| `NoteJudge.cs` | NoteJudge，池管理 |
| `../ScoringSystem/ScoringSystem.cs` | 计分系统 |
| `../ScoringSystem/JudgeRecord.cs` | 判定记录结构体 |
| `../../Data/JudgeResult.cs` | 判定结果枚举 |
| `../../../Input/Scripts/ZoneInput.cs` | 输入分区与 ZoneInput 结构体 |
