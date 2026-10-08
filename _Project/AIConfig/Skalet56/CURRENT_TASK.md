# CURRENT TASK

更新时间：2026-09-18

## 当前目标：制谱器核心交互功能

只关注制谱器（`_Project/CQ-ChartMaker/`），不关心游戏本体。

### 已完成

- 谱面数据模型（NoteUnit/BpmUnit/MotionUnit + struct Data 双层模型）
- 加载/保存（ChartParser → Unit List ↔ struct List → ChartParser.Serialize）
- 时间轴（播放/暂停/帧驱动、空格键控制）
- 滚轮控制（时间移动 ±100ms、Ctrl+滚轮 PPS 调节）
- 节拍线与细分线（BeatGridModel + PositionCalculator）
- Note 四种类型完整显示（Dot/Dash/Mute/Tune，含身体长条、Tune 方向三角）
- 单点选中/取消选中
- Inspector 面板（Note/Tune 属性查看编辑）
- 撤销/重做接入（UndoManagerModel 40步限制、IUndoableCommand + Record 模式、放置/编辑/删除全接入、Ctrl+Z/Ctrl+Shift+Z/Delete 快捷键、自动选中）
- 历史项目快捷打开（含增删、最近列表持久化）
- Note/Tune 放置、编辑、删除功能（增删改基础）
- 音频载入/播放（AudioModel + NAudio WASAPI 常驻管线、设备格式整曲缓存；audioPath.txt
    记录路径、打开项目自动加载；时间轴双向同步）
- BPM/LaneMotion编辑功能
- 放置预览功能

### 待做

#### 核心开发

**近期**

- [ ] 完善多选逻辑（框选、批量操作）

**远期**

- [ ] 实现 ViewView 游戏画面还原

#### 体验优化

**主要**

- [ ] Inspector 面板优化
- [ ] 输入框失焦优化

**次要**

- [ ] UI 缩放
