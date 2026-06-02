# NoteJudge 判定系统

## 概述

NoteJudge 负责管理所有 Note 的判定。核心由两个类协作完成：

- **Note**（及子类）——单个 Note 的判定逻辑
- **NoteJudge** ——管理四个判定池，分发输入，处理连锁

### **关键思路：**

将不同的判定行为拆分、归类，构建不同的**“判定池”**进行管理

---

## Note 类型

### Dot（点击）

- 判定窗口：`[-BadOffset, +GoodOffset]`，默认 [-150ms, +100ms]
- 击中 → 根据偏移量计算 Perfect/Early/Late/Bad → 立即提交分数
- 超时 → Miss

### Dash（长按）

- 判定窗口：`[-GoodOffset, +GoodOffset]`，默认 [-100ms, +100ms]
- 头部击中 → 计算判定结果并保存，**不提交**，进入长按池
- 长按到结束 → 提交之前保存的结果
- 中途松开 → 覆盖为 Miss 并提交
- 附带零个或多个 Tune（谱面数据中定义）

### Mute（自动长按）

- 判定窗口：`[0, +GoodOffset]`，默认 [0, +100ms]
- 不需要点击：按住按键时，到达正解时间的 Mute 自动判定为 **Perfect**
- 其余行为与 Dash 一致（长按到结束或松开）

### Tune（侧击）

- 判定窗口：`[-BadOffset, +GoodOffset]`，默认 [-150ms, +100ms]
- 附属于某个 Dash，不独立出现在谱面列表中
- 方向 Left / Right：须按下 Dash 所在轨道左侧 / 右侧的任意按键
- 击中 → 计算判定结果 → 立即提交

---

## 四个判定池

| 池 | 存放 | 判定方式 |
|----|------|----------|
| `_dotDashHeadPool` | Dot、Dash | 点击，一次只判一个 |
| `_muteHeadPool` | Mute | 宽松判定（按住自动判） |
| `_sustainPool` | Dash、Mute（头部判定后） | 检测松开和长按结束 |
| `_tunePool` | Tune | 点击，一次只判一个，与 Dot 池并行 |

**跨池并行**：同一帧内，一个按键可以同时击中 Dot/Dash 池的一个 Note 和 Tune 池的一个 Note。

---

## 完整判定流程（每帧）

```
1. CalcDeltaTime → 计算帧间时间差

2. UpdateMuteTimer
   - 遍历 Hold 输入 → 刷新对应轨道的计时器（= GoodOffset）
   - 所有计时器递减 deltaTime
   
3. UpdateMuteHeadPool
   - 遍历每个轨道 → 计时器 > 0 的轨道
   - 从 muteHeadPool 中找 CanBeHit 的 Mute → 自动击中 → 移入 sustainPool
   
4. 点击判定（并行）
   - TryHitDotDash：遍历 Press 输入 → 击中一个 Dot 或 Dash → Dash 额外移入 sustainPool
   - TryHitTune：同样的 Press 输入 → 击中一个 Tune
   
5. UpdateSustainPool
   - 长按结束（IsSustainEnded）→ SubmitFinal → 移除
   - 未按住（!IsLaneHeld）→ HandleMiss → 移除
   
6. CheckMissAll
   - 收集所有池中超时的 Note（IsMissed）
   - 统一处理 HandleMiss + 连锁 + 移除
```

---

## Miss 连锁

Dash 和 Tune 之间有双向连锁，只有 **Miss** 会传播：

### Dash Miss → 连锁所有 Tune

```
Dash Miss → 遍历 GetLinkedTunes()
  → 仍在 _tunePool 中的 Tune → OnMiss + 移除
```

已判定（不在池中）的 Tune 不受影响。

### Tune Miss → 连锁 Dash → 连锁其他 Tune

```
Tune Miss → 取 GetLinkedDash()
  → Dash 仍在池中？→ Dash.OnMiss → 从池移除
  → 遍历 Dash 的其余 Tune → OnMiss + 从 _tunePool 移除
```

Dash 已完成长按（已结算、不在池中）时不连锁。防止双重提交。

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
| `AddNote(NoteData)` | 创建 Note 并分配到对应池，Dash 连带创建 Tune |
| `NotesUpdate(inputs, time)` | 常规帧更新 |
| `AutoPlayUpdate(time)` | AutoPlay 帧更新 |

---

## 数据流

```
谱面文本 → ChartParser.Parse → List<NoteData>
  → TrackInstaller → ChartSheet → ScoringSystem
  → NoteJudge.AddNote → 创建 Note → 分配池

游戏循环：
  音频时间 → NoteJudge.NotesUpdate(inputs, time)
    → 各池判定 → Note.OnHit / Note.OnMiss / Note.SubmitFinal
      → Note.SubmitResult → ScoringSystem.AddRecord
```

---

## 相关文件

| 文件 | 说明 |
|------|------|
| `Note.cs` | Note 抽象基类 |
| `DotNote.cs / DashNote.cs / MuteNote.cs / TuneNote.cs` | 四种子类 |
| `NoteJudge.cs` | NoteJudge，池管理与连锁 |
| `../ScoringSystem/ScoringSystem.cs` | 计分系统 |
| `../ScoringSystem/JudgeRecord.cs` | 判定记录结构体 |
| `../../Data/JudgeResult.cs` | 判定结果枚举 |
| `../TrackController/LaneInput.cs` | 轨道输入结构体 |
