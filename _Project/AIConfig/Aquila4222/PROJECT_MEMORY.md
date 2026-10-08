# PROJECT MEMORY

跨会话持久记忆，每周更新一次。

## 项目定位
7K PC 下落式音游，Unity 2022.3 + Zenject DI + Wwise 音频（FMOD、CriAudioPlayer 暂时搁置保留）。附带 .NET 制谱器。旧项目原型：`D:/GitHub/MGFrame`。

## 目录结构
- `Assets/Common/SharedScripts/` — Unity & .NET 制谱器共享（`CQMusicGame.Shared`，纯 C#，无 UnityEngine 引用）
- `Assets/Common/Scripts/` — Unity 专用公共工具
- `Assets/Features/GamePlay/` — 打歌局内
- `Assets/Features/TrackService/` — 乐曲加载与选歌
- `Assets/Common/Test/` — 测试脚本
- `_Project/Docs/` — 项目文档
- `_Project/CQ-ChartMaker/` — 制谱器

## 依赖注入规范
- MonoBehaviour 场景对象：`[SerializeField]` 手拖 + `BindInstance` 绑定
- 纯 C# 类：`InstallBindings` 里 new 后 `BindInstance`
- 跨场景服务用 ProjectContext
- ScriptableObjectInstaller 注册官谱资产（TrackAssetInstaller）和 NoteJudge 配置（NoteJudgeConfigInstaller）到 DI

## 核心架构决策
- Note 三层分离：`NoteData` → `Note` → `NoteView`
- Note 判定时全部加载，不中途增删
- NoteView 按 `AppearOffset` 延迟生成，生成时查 `NoteJudge` 绑定回调
- 计分整数运算，满分 1,000,000
- 轨道移动：`LaneMotionData` → `LaneKeyframe`（PreCurve 回退链）→ `LaneState`
- Note 视觉：MeshRenderer + 双 Submesh（submesh 0 描边底、submesh 1 填充顶），世界坐标。程序化 Mesh 生成（圆/折线），静态材质资产 `[SerializeField]` 手拖，`sharedMaterials` 赋值
- Sorting Layer 五层（底→顶）：Track → DashBody → Mute → Note → VFX。Layer 在 prefab 设，Order 代码设（Note 层 `-TargetTime`，DashBody/Mute 层 `TargetTime`）
- DashView 用两个子 GO（Head/Body）分属不同 Sorting Layer，头圆显示在身子上
- 对象池：`ObjectPool<T> where T : Component, IPoolable`，Stack 实现
- 特效：Canvas Image，独立于 NoteView
- 旧谱面转换规则（MGFrame 4K → 新格式）：`t`→Dot、`h`→Dash、`d`→Mute（箭头 `↑↓←→` 为废案）；时间/时长 秒→毫秒四舍五入；轨道 0-3 → -2/-1/1/2；缺省 `Appear:-3000`、`Speed:Default()`。注意仓库中 `Old_ChartParser.cs` 是半成品，勿直接采用
- 轨道移动数据整表为空时（旧谱面场景），`LaneState` 按默认布局兜底：各轨 `Time:-1、(i*100,-300)`

## CLAUDE.md 索引
- `_Project/Docs/CQProject 数据设定.md` — 数据格式大全
- `Assets/README[相关文档].md` — 所有文档链接
