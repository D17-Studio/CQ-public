# Assets 目录结构规范

本文档定义了本项目 `Assets/` 文件夹的组织规则。所有成员必须遵守，以保证项目可维护性和团队协作效率。

> 该文档仅作为了解项目大致Assets目录，不代表目录的具体结构

---

## 一、顶层目录概览

```tex
Assets/
├── Common/          # 跨模块共享的全局资源
├── Features/        # 按业务功能划分的模块（核心业务代码）
├── Content/         # 游戏内容数据
├── ThirdParty/      # 手动导入的第三方插件（非 UPM）
└── README.md        # 本目录的快速索引
```

---

## 二、各目录详细说明

### 1. `Common/` - 共享资源

存放被多个模块共同使用的资源，应保持“与具体业务无关”。

| 子目录       | 用途                             | 示例                                             |
| ------------ | -------------------------------- | ------------------------------------------------ |
| `Scripts/`   | 通用工具类、扩展方法、基础框架   | `Singleton.cs`, `ExtensionMethods.cs`            |
| `Settings/`  | 全局配置资源（ScriptableObject） | `GameSettings.asset`, `URP_GlobalSettings.asset` |                          |
| `Scenes/`    | 全局场景（启动场景、主菜单）     | `Boot.unity`, `MainMenu.unity`                   |

**约束**：`Common` 中的资源不应依赖任何 `Features` 中的内容。

---

### 2. `Features/` - 功能模块

每个子文件夹代表一个独立的游戏功能（如 `Player`, `UI`, `Inventory`）。模块内部应高内聚，包含该功能所需的所有资源。

**标准模块结构**：

```
Features/
└── [ModuleName]/
│   └── Scripts/          		# 运行时脚本
│   │   └── Core/         		# 核心逻辑（状态机、管理器）
│   │   ├── Data/         		# ScriptableObject 配置
│   │   └── UI/           		# 模块专属 UI 逻辑
│   ├── Prefabs/          		# 模块相关预制体
│   ├── Animations/       		# 动画控制器、动画片段
│   ├── Materials/        		# 模块专用材质
│   ├── Textures/         		# 模块专用纹理
│   ├── Audio/            		# 模块专属音效
│   ├── Scenes/           		# 模块专属场景（可选）
│   ├── Editor/           		# 模块专属编辑器扩展
│   └── [ModuleName].asmdef     # 程序集定义文件（推荐）
```
---

### 3. `Content/` - 游戏内容资源

存放所有**数据驱动的游戏内容**，如曲目、角色、装备、关卡等。这些资源与功能代码解耦，便于批量管理和热更新。

**子目录结构**（目前只需存放曲目）：

```
Content/
└── Tracks/ # 可游玩的曲目（音游核心内容）
```
**设计原则**：

- 每个内容项使用独立文件夹，内部包含其所有关联资源（模型、纹理、音频、配置等）。
- 使用 `ScriptableObject` 作为数据容器（如 `TrackData.asset`），方便编辑器编辑和代码引用。
- 内容目录与 `Features` 目录严格分离，内容不包含业务逻辑，逻辑由功能模块实现。

---

### 4. `ThirdParty/` - 第三方插件

存放通过 `.unitypackage` 手动导入的插件。每个插件应保留在独立子目录中，不要修改插件原始文件（若需定制，通过继承或封装实现）。

示例：

```
ThirdParty/
├── DOTween/
├── TextMeshPro/
└── CRIMW/
```

---

## 三、特殊文件夹规则

| 文件夹名          | 允许位置                        | 说明                                                         |
| ----------------- | ------------------------------- | ------------------------------------------------------------ |
| `Editor`          | **任意子目录**                  | 存放编辑器扩展脚本，可放在模块内（如 `Features/Player/Editor/`）。 |
| `Plugins`         | **仅 `Assets/Plugins`**         | 存放原生插件（DLL、.a、.so）。                               |
| `StreamingAssets` | **仅 `Assets/StreamingAssets`** | 存放保持原始格式的二进制文件。                               |
| `Gizmos`          | **仅 `Assets/Gizmos`**          | 存放 Scene 视图图标。                                        |

