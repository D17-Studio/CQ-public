# PROJECT MEMORY

跨会话持久记忆。只关注制谱器，游戏本体不关心。

## 项目定位

CQ-ChartMaker：Avalonia .NET MVVM 桌面制谱器。为 7K PC 下落式音游制作谱面。旧项目原型：`D:\GitHub\MusicGameFramework2\MusicGameFramework`。

## 目录结构

- `_Project/CQ-ChartMaker/CQ-ChartMaker/Views/` — 所有 View（.axaml + .axaml.cs）
- `_Project/CQ-ChartMaker/CQ-ChartMaker/ViewModels/` — 所有 ViewModel
- `_Project/CQ-ChartMaker/CQ-ChartMaker/Models/` — 数据模型、计算模块
- `_Project/CQ-ChartMaker/CQ-ChartMaker/Common/` — 公共工具
- `Assets/Common/SharedScripts/` — Unity & 制谱器共享（`CQMusicGame.Shared`，纯 C#，无 UnityEngine 引用）
- `_Project/Docs/` — 项目文档

## View ↔ ViewModel 嵌套树（已定型，不再改动）

```
MainWindow
├── ChartView ────────────── ChartVM
│   ├── ViewView ─────────── ViewVM ─── Timeline (Model)
│   ├── EditorView ───────── EditorVM
│   │   ├── NotesView ────── NotesVM
│   │   ├── LanesView ────── LanesVM
│   │   └── BeatsView ────── BeatsVM
│   └── InspectorView ────── InspectorVM
└── MusicInfoView ────────── MusicInfoVM
```

## 依赖注入规范

- MainWindowViewModel 在 App.OnFrameworkInitializationCompleted 中创建，构造函数 = 初始化入口
- 子 ViewModel 由父 ViewModel 在构造函数中 `new` 创建
- **Model 由用到它的最高级 ViewModel 创建**，通过属性注入向下传递
- 使用**属性注入**（`{ Property = xxx }`），不用构造函数注入
- 不使用 DI 容器，手动组装

## 核心架构决策

- Unit 是制谱器编辑主体（class 引用），struct Data 是只读数据源
- BeatGridModel 等计算模块从 `ChartSheet.GetXxxList()`（struct）读数据
- `SelectedUnit` 统一管理在 ChartViewModel
- 命名空间仅 4 个：Views / ViewModels / Models / Common
- CommunityToolkit.Mvvm source generators：`partial class` + `[ObservableProperty]` + `[RelayCommand]`
- ViewModelBase (abstract, extends ObservableObject) 是所有 VM 的基类
- View code-behind 极简，只 `InitializeComponent()`
- 导航用 `CurrentView`/`CurrentSubView` + bool 属性 + ContentControl + DataTemplate
- 增删改通过 `ChartViewModel` 统一入口（AddNote/AddTune/DeleteSelected），Inspector 编辑使用暂存命令 + 失焦提交模式
- 撤销/重做采用命令模式（`IUndoableCommand`），`UndoManagerModel.Record()` 只入栈不执行，`ChartViewModel` 统一管理刷新和自动选中

## 代码风格
- 中文注释
- 公开方法加 `<summary>` 注释
- 命名简洁，对齐现有风格
