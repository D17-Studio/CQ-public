# CQ-public · 音游核心代码

> 本项目是代号 **CQ** 的音游核心代码，游戏本身为**非商业项目**，仅供学习、交流与展示。
> 代码开源旨在分享架构设计与开发经验，欢迎感兴趣的朋友参考、讨论。

---

## ⚠️ 重要说明

- **包含内容**：C# 源码、Shader、预制体、场景、ScriptableObject 配置、`.meta` 文件、文档
- **不包含内容**：贴图纹理、音频文件、3D 模型、字体、视频等美术/音乐资产
- **第三方闭源 SDK**（CRIWARE ADX、FMOD Studio）不在仓库中，相关封装代码**可以阅读但无法编译**运行，需自行获取 SDK
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
├── Common/          # 通用框架（单例、对象池、输入、存档、屏幕适配等）
├── Features/        # 游戏功能（谱面解析、判定系统、计分、音符渲染等）
├── ThirdParty/      # 第三方库
│   ├── UniTask/     # MIT · 异步工具链
│   └── Zenject/     # MIT · 依赖注入
└── ...
_Project/
├── CQ-ChartMaker/   # 谱面编辑器（Avalonia 桌面应用）
└── Docs/            # 架构文档
```

详细请参考：[项目 README](./Assets/README[相关文档].md)

---

## 📄 许可证

| 内容 | 协议 |
|------|------|
| 代码（本仓库） | **MIT** |
| 完整游戏（含资源） | 保留所有权利，不开源 |
| UniTask | MIT |
| Zenject | MIT |
| CRIWARE / FMOD | 须遵守各自许可协议 |

---

## 🔧 开发环境

- Unity 2022.3.62f2c1
- CRIWARE ADX（自行获取）
- FMOD Studio（自行获取）

---

## 🙌 致谢

- [CRIWARE](https://www.criware.cn/)
- [FMOD](https://www.fmod.com/)
- [UniTask](https://github.com/Cysharp/UniTask)
- [Zenject (Extenject)](https://github.com/modesttree/Zenject)
- 团队成员的付出与协作

---

## 📬 联系

- 团队邮箱：d17studio@outlook.com
