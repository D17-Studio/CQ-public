# Persistence 存档模块（分数 / 设置）

## 概述

本地持久化模块：两套独立 store，各管 `Application.persistentDataPath` 下的一个单文件。

| | 分数存档 `ScoreArchive` | 设置存档 `PlayerConfigStore` |
| --- | --- | --- |
| 数据文件 | `scores.json` | `playerConfig.json` |
| 数据粒度 | 每谱面一条记录（按 `Key` 索引） | 单条配置（全局流速 / 输入偏移） |
| 写入时机 | 写透——每局一次，写入即落盘 | 检查点——面板关闭 `Save()`，应用退出兜底 |
| 文件形态 | Base64 + HMAC 签名（防手改） | 明文 JSON |
| 消费方 | 结算写入 / 选歌回显（已接入并实机验证，2026-10-15） | 设置功能（待做，暂无消费方） |

- 位置：`Assets/Features/Persistence/Scripts/`，纯 C#、无场景依赖；均为 DI 绑定的跨场景单例（SO installer 挂 ProjectContext，asset 在 `Settings/`）。
- 存储路径均为 `Application.persistentDataPath`（Windows 位于 `%USERPROFILE%\AppData\LocalLow\公司名\产品名\`）。
- 两套 store **刻意各自独立实现**——共享的只是"文件机制"层面的做法（见"共同存取机制"）；通用文件层（`JsonFile`）等第三个消费方出现、或文件机制出过一次问题再收口。
- 与现有 `SaveManager` 无关（不引用、不共享机制）。
- **接入状态（2026-10-15）**：分数档全链路已接入并实机验证（写入 → 结算 NEW BEST → 选歌详情回显）；设置档存储完成，等设置功能接线（接线点见 `_Project/AIConfig/Aquila4222/CURRENT_TASK.md`）。

---

## 设计取向

1. **按需裁剪，不预先通用化**——两套 store 各做各的，不做"大一统保存器"。
2. **可变状态不出门**——外部拿到的只有只读快照（`ScoreView`）或值属性，改数据必须走显式入口。
3. **磁盘格式简单且可自愈**——单文件、好备份、坏了能保底重来（见"共同存取机制"）。
4. **身份与存储解耦**（分数档）——谱面身份是 JSON 里的 `Key` 字段，不是文件名 / 目录名，命名格式无任何文件系统约束。

---

## 共同存取机制

- **原子写**：先写 `xxx.tmp` 再替换正式文件（首写走 `File.Move`，已存在走 `File.Replace`）——防"写一半被杀进程 / 断电"，这是唯一真会吃掉数据的场景。
- **磁盘状态三分支**（两套同款）：

  | 磁盘状态 | 行为 |
  |---------|------|
  | 文件不存在 | 空档 / 默认值启动（正常状态，不产生备份） |
  | 损坏 / 解析失败（分数档另含验签不符） | 备份为 `xxx.bak` 留证（不删）→ 空档 / 默认值继续 |

- **写失败不丢数据**：写盘异常只记日志不崩溃；内存状态仍正确，下次成功写入全量补写（设置档另保脏标记等重试）。
- **Version 钩子**：两份数据都带 `Version` 字段（迁移钩子）——旧档尽力解析（缺字段回落默认值、更高 Version 也尽力读）。

---

## 一、分数存档（ScoreArchive）

记录每个谱面的最佳分数与全连成就，支撑"打歌出分 → 结算写入 → 读取回显"链路。

### 文件外壳（防手改）

`scores.json` 不能直读，外层是 Base64 + 签名：

```json
{ "Payload": "<ScoreArchiveData JSON 的 Base64>", "Mac": "<HMAC-SHA256 签名>" }
```

加载顺序：解壳 → 验证签名（`Mac` 与文件里未解码的 `Payload` 原文比对）→ 通过后才解码解析。

### 数据内容

```csharp
[Serializable] class ScoreArchiveData { int Version; List<ScoreRecord> Records; }
[Serializable] class ScoreRecord { string Key; int BestScore; bool IsFullCombo; }
```

`Key` = 谱面索引（见"索引口径"）；`IsFullCombo` = 全连成就，**曾经达成即永久点亮**。

### 为什么单文件

写入频率 = 每打完一局一次，数据量 = 每谱面几十字节。这个量级下"每 key 一文件"没有收益，反而要付 key 洗成文件名的代价（清洗非法字符、防空 key 撞车）；单文件全量重写更简单，且好备份、好排查（玩家发一个文件即可），开发期删文件即重置。

### 公开 API

| 成员 | 类型 | 说明 |
|------|------|------|
| `ScoreArchive(string filePath)` | 构造 | 构造即加载；之后查询全部走内存 |
| `GetBest(string key)` | 方法 | 纯查询，返回只读快照，不交出内部引用 |
| `RecordScore(string key, int score)` | 方法 | 写入分数（哑存储），立即原子落盘 |
| `RecordFullCombo(string key, bool isFullCombo)` | 方法 | 写入全连状态（哑存储），独立写口，与分数互不触碰 |
| `ScoreView.Exists` | 属性 | 是否打过（区分"没打过"与"打了 0 分"） |
| `ScoreView.BestScore` / `IsFullCombo` | 属性 | 最佳分数 / 全连成就 |
| `ScoreKeys.Of(TrackData, ChartData)` | 静态方法 | 由曲目 + 谱面拼出索引 `Key`（唯一落点） |

### 索引口径（ScoreKeys）

```
key = MusicTitle.Trim() + "_" + 难度代号 + "_" + 存档签名
```

- **难度代号 = 映射表字面量**（现为 `Dif0`~`Dif3`）：与枚举名 / 顺序解耦——枚举改名、重排、插新难度都不影响旧档；**发布后不可改**，新增难度在 `ScoreKeys` 补一条。
- **存档签名 = track 级 `TrackData.ArchiveSignature`**：官谱来自 `OfficialTrackInfo.ArchiveSignature`（SO 手填，可空）；自制谱留空。**签名为空时保留末尾 `_`**（不省略），键形如 `曲名_Dif0_`。
- **身份政策（2026-11-13 修订）**：官谱 = 乐曲名 + 可选签名；自制谱 = 签名留空（同名自制曲共用键，已接受）；需要冻结 / 防撞 = 手填签名。
- 脏字符不清洗（key 只是 JSON 字符串，清洗反而制造碰撞），仅 `Trim()` 防首尾空白手滑。
- **格式修订（2026-11-13）**：签名从谱面级上移曲目级，并作为第三段拼入；旧键（`曲名_DifN` / `ArchiveId_DifN`）全部失联，alpha 期不迁移、不兼容。

### 使用方式

**写入规则住在结算处（不在存档类）**：`ScoreArchive` 是哑存储——传什么记什么，不做取优、不做校验。取优判断在调用方：

```csharp
string key = ScoreKeys.Of(track, chart);
var old = archive.GetBest(key);
if (!old.Exists || score > old.BestScore)
    archive.RecordScore(key, score);     // 相等不算新纪录，不写
if (fc && !old.IsFullCombo)
    archive.RecordFullCombo(key, true);  // 只在首次达成时写入
```

语义约定：

- **FC = 曾经达成**：一旦点亮永不熄灭（成就语义）；由 `RecordFullCombo` 独立写口承载、不触碰分数——"未破纪录但首次 FC"只更新 FC，旧最佳分保留。
- **AllPerfect 不存标志**：`AllPerfect ⇔ Score == 1,000,000`（与 `ScoringSystem.FullMarks` 一致），由分数判断。
- **有效成绩口径 = 自然曲终**：中途暂停返回不写。
- **写透落盘**：两个写口内部改完即保存，外部不存在"忘了落盘"的可能。
- **查询零副作用**：只浏览不写盘，文件不会因 `GetBest` 而产生或更新。

### 防手改与密钥

- **轻量档防手改**：Base64 让文件不能直读 + HMAC 签名识别篡改。威胁模型只防"手痒改档"两档：① 打开见乱码不折腾 ② 会解码、改完编码回去——被签名识破。**不防**反编译、内存修改（视为黑客，接受）；真防护 = 服务端校验，联网功能出现时再议。
- **密钥**：`ScoreArchiveKey.cs` 独立文件存单串随机常量；已加入 `sync-public.sh` 排除清单（**文件若迁移，排除清单须跟着改**）。发布后不可更换密钥（换 = 全部旧档验签失败被重置；alpha 期除外）。

### 已知边界（接受）

孤儿记录不清理（曲目改名后旧记录留存）；多实例同跑 = 后写覆盖（单机单实例假设）；不做"上一份好档"轮转（原子写已防写坏，分数可再生）。

---

## 二、设置存档（PlayerConfigStore）

### 数据与文件

```csharp
[Serializable] class PlayerConfig { int Version; float ScrollSpeed; int InputOffsetMs; }
```

- `ScrollSpeed` 默认 1000（= 现行 `NotePositionCalculator` 装配值，接线后行为零变化）；`InputOffsetMs` 默认 0。
- `playerConfig.json` 是**明文 JSON、不签名**：防手改是分数档的专属威胁模型；设置可读反而利于排障。

### 公开 API

| 成员 | 类型 | 说明 |
|------|------|------|
| `PlayerConfigStore(string filePath)` | 构造 | 构造即加载；缺档 = 默认值，坏档 = 备份 + 默认值 |
| `ScrollSpeed` | 属性 | 玩家全局流速；get 查询，set 内存即时生效 + 标脏 |
| `InputOffsetMs` | 属性 | 输入偏移（毫秒，可正可负）；同上 |
| `Save()` | 方法 | 检查点落盘：脏才写；写失败保脏等下次 |

`PlayerConfig` 实例不出门——外部只走属性；`Application.quitting` 兜底已内置在 store 内。

### 使用方式（检查点模式）

```csharp
// 读：打歌装配时灌给 NotePositionCalculator.GlobalSpeed（接线待做）
float speed = store.ScrollSpeed;

// 改：设置面板拖动——只改内存，即时生效，不碰磁盘
store.ScrollSpeed = 1200f;
store.InputOffsetMs = -20;

// 存：面板关闭时的检查点（应用退出另有自动兜底）
store.Save();
```

语义约定：

- **修改 ≠ 落盘**：与分数档的"写透"相反——设置是高频修改，走检查点；禁止每次改动写盘。
- **同值写入跳过**：set 同值不标脏，`Save()` 脏才写。

---

## 相关文件

- 分数档：`ScoreArchive.cs`（内核）/ `ScoreKeys.cs`（索引唯一落点）/ `ScoreRecord.cs` / `ScoreArchiveData.cs` / `ScoreView.cs` / `ScoreArchiveKey.cs`（签名密钥，公开库同步排除）/ `ScoreArchiveInstaller.cs`
- 设置档：`PlayerConfig.cs`（数据）/ `PlayerConfigStore.cs`（store）/ `PlayerConfigInstaller.cs`
- installer asset：`../Settings/ScoreArchiveInstaller.asset` / `../Settings/PlayerConfigInstaller.asset`
- [CQProject-存档系统设计.md](../../../../_Project/Docs/CQProject-存档系统设计.md) — 分数档完整设计依据与决策记录
