# CURRENT TASK

更新时间：2026-10-08

## 当前任务：

使用Wwise完成音频播放系统

## 其余任务

### 已完成

- NoteView 全部骨架（基类 + Simple/Sustain + 4 叶子类）
- NoteViewSpawner 按 AppearOffset 生成
- ObjectPool、IPoolable、ScreenAdapter（坐标转换）
- HitEffect（缩放淡出动画）、BadEffect（空框架）、VFXManager
- PlayHitEffect 四个 View 均已接入 VFXManager
- NoteData / ChartParser / 测试全更新（AppearOffset、Note 不透明度、BPM）
- ChartException 重命名
- LaneMotionEffect → LaneMotionData、BpmPoint → BpmData、TransformCurve → LaneKeyframe、LaneTransform → LaneState
- LaneView + LaneViewManager（SpriteRenderer + 世界坐标）
- LaneMotion 加 AffectNote（Note 不透明度）
- KeyNameDisplay 按键名格式化
- TrackService 乐曲加载体系（TrackInfo、ChartInfo、OfficialTrackAsset、TrackAssetInstaller、TrackCollection、TrackSelectionService）
- SpriteRenderer 迁移（NoteView + LaneView 全部脱离 Canvas）
- CLAUDE.md、AGENTS.md、PROJECT_MEMORY.md 建立
- Note 外观 MeshRenderer 双 Submesh 渲染（4 种形状）
- 5 个 Sorting Layer + Order 规则
- NoteMeshGenerator（BuildCircle / BuildPolyline / BuildArrow，miter join + bevel fallback）
- Tune 方向指示箭头 — BuildArrow（凸角 miter/凹角 bevel → triangle fan），Arrow0/1/2 子 GO，每帧 UpdateArrowTransforms
- 旧谱面转换完成（MGFrame 3 曲 × 3 难度共 9 张 → 新格式：t/h/d → Dot/Dash/Mute，秒→毫秒四舍五入，轨道 0-3 → -2/-1/1/2；产物在 `_Project/ConvertedCharts/`）
- LaneState 整表为空时默认布局兜底（旧谱面不含轨道数据，补 `Time:-1、(i*100,-300)`）

### 待做

- [ ] 完善wwise的乐曲播放
- [ ] 制作新版Note视觉效果
- [ ] 制作NoteView的Miss效果
- [ ] BadEffect 填充实际动画
- [ ] Note 不透明度实际应用到 NoteView
