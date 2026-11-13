# CQ-public · 音游核心代码

> 本项目是代号 **CQ** 的音游核心代码，游戏本身为**非商业项目**，仅供学习、交流与展示。
> 代码开源旨在分享架构设计与开发经验，欢迎感兴趣的朋友参考、讨论。

---

## ⚠️ 重要说明

- **包含内容**：C# 源码、Shader、预制体、场景、ScriptableObject 配置、`.meta` 文件、谱面文本与文档
- **不包含内容**：贴图纹理、音频文件、3D 模型、字体、视频等美术/音乐资产
- **不含闭源 SDK 本体**：Audiokinetic Wwise、CRIWARE ADX 均不在仓库中，相关封装代码**可以阅读但无法编译**运行，需自行获取 SDK
- **由私有库自动同步**：本仓库通过 `_Project/sync-public/sync-public.sh` 从私有库生成，仅同步 `Assets/` 与 `_Project/`，不包含 `Packages/`、`ProjectSettings/`，因此**无法直接打开或构建**
- 本项目**未使用任何传染性开源协议（如 GPL）**

---

## 🎯 开源目的

- 展示项目的**代码架构**与**设计模式**
- 分享音游开发经验（节奏判定、音频管理、输入处理、谱面系统等）
- 为其他开发者提供参考与讨论

---

## 📁 项目结构

```
Assets/
├── Common/          # 通用框架（单例、对象池、输入、存档、屏幕适配、音频播放封装等）
├── Content/         # 内置乐曲配置（ScriptableObject）与谱面文本
├── Features/        # 游戏功能（谱面解析、判定、计分、音符渲染、音频服务、存取档等）
├── Resources/       # 场景启动依赖
├── StreamingAssets/ # Wwise 生成信息（不含音频文件）
└── ThirdParty/
    ├── UniTask/     # MIT · 异步工具链
    ├── Zenject/     # MIT · 依赖注入
    └── Fonts/       # SIL OFL 1.1 · 思源黑体
_Project/
├── CQ-ChartMaker/   # 谱面编辑器（Avalonia 桌面应用）
├── Charts/          # 谱面文本
├── Design/          # 系统设计文档
├── Docs/            # 架构与开发文档
└── sync-public/     # 公开库同步脚本与说明
```

详细请参考：[项目 README](<./Assets/README[相关文档].md>) · [同步机制](./_Project/sync-public/README.md)

---

## 📄 许可证

| 内容 | 协议 |
|------|------|
| 代码（本仓库） | **MIT** |
| 完整游戏（含资源） | 保留所有权利，不开源 |
| UniTask | MIT |
| Zenject | MIT |
| 思源黑体 | SIL OFL 1.1 |
| Wwise / CRIWARE ADX | 须遵守各自许可协议（SDK 本体不在本仓库中） |

---

## 🔧 开发环境

- Unity 2022.3.62f2c1
- Audiokinetic Wwise 2025.1.10（Wwise Unity Integration 2025.1.10.4304，自行获取）
- CRIWARE ADX（自行获取）
- UniTask 2.5.10
- .NET 10（谱面编辑器）

---

## 🙌 致谢

- [Audiokinetic Wwise](https://www.audiokinetic.com/)
- [CRIWARE](https://www.criware.cn/)
- [UniTask](https://github.com/Cysharp/UniTask)
- [Zenject (Extenject)](https://github.com/modesttree/Zenject)
- [思源黑体 Source Han Sans](https://github.com/adobe-fonts/source-han-sans)
- 团队成员的付出与协作

---

## 📬 联系

- 团队邮箱：d17studio@outlook.com
