# NoteView 视觉系统

## 类层次

```
NoteView (abstract, MonoBehaviour, IPoolable)
  ├── SimpleNoteView (abstract)
  │     └── DotView      — 白色实心圆 + 黑色描边
  └── SustainNoteView (abstract)
        ├── DashView     — 头圆（Note层）+ 身子折线（DashBody层）
        └── MuteView     — 黑色矩形 + 白色描边（无头），Mute层
```

> Tune 没有独立视觉对象：仅作为 Dash 身子折线的几何控制点存在。

## 渲染方案

统一 **MeshRenderer + 双 Submesh**，不使用 Sprite。

| Submesh | 材质索引 | 层级 | 形状 | 用途 |
|---------|---------|------|------|------|
| 0 | `sharedMaterials[0]` | 底 | 同形加宽 `2×strokeWidth` | 描边色 |
| 1 | `sharedMaterials[1]` | 顶 | 原尺寸 | 填充色 |

- 圆：submesh 0 大圆、submesh 1 小圆，共享中心顶点
- 折线：submesh 0 宽线（描边）、submesh 1 窄线（填充），每个 segment 一个 quad
  - 旧方法（`BuildPolyline(..., miterLimit)`）：转角 miter join + bevel fallback
  - 圆角方法（`BuildPolyline(mesh, points, width, strokeWidth)`，Dash 使用）：不做 join，线段平切，折点用圆补角——填充圆直径 = `width`，描边圆直径 = `width + 2×strokeWidth`

材质用两个静态资产 `NoteWhite.mat` / `NoteBlack.mat`（URP Unlit），通过 `[SerializeField]` 手拖到 prefab，代码里 `sharedMaterials` 赋值，零运行时分配。

## Sorting Layer（底→顶）

| Layer | 内容 | Order 规则 |
|-------|------|-----------|
| `Track` | 轨道竖线 | — |
| `DashBody` | Dash 折线身子 | `TargetTime`（后生成在上） |
| `Mute` | Mute 矩形 | `TargetTime`（后生成在上） |
| `Note` | Dot / Dash头 | `-TargetTime`（先生成在上） |
| `VFX` | 特效 | — |

Layer 在 prefab Inspector 上手设，Order 在 `OnSpawn` 中代码设。

## 生命周期

```
ObjectPool.Get()
  └→ Awake()                         // 仅首次，transform 移到屏幕外
Initialize(data, calc, ...)
  └→ OnSpawn()                       // 每次取用
       ├→ SetupMeshRenderer()        // 确保 MeshFilter+MeshRenderer，设 sharedMaterials
       └→ BuildMesh()                // abstract，子类生成 mesh
SetActive(true)
Update()                              // 每帧
  ├→ CalculatePositions()            // 填充 PositionBuffer
  └→ UpdateVisual()                  // 更新 transform + mesh
```

- **Dot**：mesh 在 BuildMesh 生成一次，不改；UpdateVisual 只移 transform
- **Mute/Dash body**：mesh 每帧 `Clear()` + 原地重写，无额外分配

## 坐标与位置

- `NotePositionCalculator.Calculate()` 返回 `List<Vec2>`（设计空间坐标）
- `ScreenAdapter.ToWorldPos()` 转为 Unity 世界坐标
- Mesh 顶点用世界坐标的局部偏移（`worldPos - headWorldPos`），transform.position 设在 headWorldPos

## DashView 子对象

DashView 使用两个子 GameObject 分别在不同 Sorting Layer：

| 子对象 | 组件 | Sorting Layer | 形状 | mesh 更新 |
|--------|------|--------------|------|----------|
| `Head` | MeshFilter + MeshRenderer | `Note` | 圆 | 生成一次 |
| `Body` | MeshFilter + MeshRenderer | `DashBody` | polyline | 每帧重建 |

运行时通过 `[SerializeField]` 引用子对象组件。

## NoteMeshGenerator

`Assets/Features/GamePlay/Scripts/NoteView/MeshGeneration/NoteMeshGenerator.cs`

两个 public static 方法：

- `BuildCircle(Mesh, radius, segments, strokeWidth)` — 双 submesh 三角形扇
- `BuildPolyline(Mesh, points, width, strokeWidth, miterLimit)` — 双 submesh 折线，每段一个 quad

Polyline 处理：
- 连续重合点（距离² < 1e-4）自动合并
- 转角处 miter join，超出 miterLimit 时走 bevel（三角形填充）
- stroke 子 mesh 端点沿线段方向延伸 `strokeWidth`，形成完整四边描边
