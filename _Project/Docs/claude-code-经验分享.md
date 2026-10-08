# Claude Code 经验分享

## 安装skill：

### 1. ECC（Everything Claude Code）

**是什么**：Claude Code 的通用编程能力增强套件，200+ 技能、28+ 子智能体、60+ 斜杠命令。

**安装方式**：

```bash
/plugin marketplace add https://github.com/affaan-m/ECC
/plugin install ecc@ecc
/plugin reload
```

**主要功能**：

- `/plan` — 制定分步实施计划（等价于 Shift+Tab 进入 Plan Mode）
- `/code-review` — 审查本地改动或 GitHub PR
- `/tdd` — 测试驱动开发流程
- `/build-fix` — 自动修复编译错误
- `/security-scan` — 安全扫描
- 多语言专项审查（Python、Go、Rust、C++、React、Flutter 等）

**适用场景**：所有编程任务，提升 Claude 的代码质量、规划和审查能力。

---

### 2. Unity-Skills

**是什么**：Unity 编辑器与 Claude Code 的桥接工具，让 AI 直接操控场景、编译、跑测试。

**安装方式**：

1. Unity Package Manager → Add package from git URL：(项目中已安装)
   ```
   https://github.com/Besty0728/Unity-Skills.git?path=/SkillsForUnity
   ```
2. Unity 菜单 `Window → UnitySkills → Start Server` 启动服务
3. Unity 菜单 `Window → UnitySkills → Skill Installer` → Claude Code → Install

**主要功能**：
- 场景操作：创建/修改 GameObject、调组件、编 Prefab、改材质
- 编辑器控制：编译、跑测试、Play/Stop、截图、读 Console 日志
- 领域知识：68 个 Unity 专项技能文档（ShaderGraph、动画、UI、寻路等）
- 权限模式：齿轮图标切换 Auto/Approval/Bypass

**适用场景**：Unity 游戏开发，让 Claude 从"只写代码"升级为"能操作编辑器"。

**注**：启动 Claude Code 时，工作目录必须在 Unity 项目根目录（包含 `Assets/` 的目录）

---

## 写项目文件：

| 文件 | 用途 |
|------|------|
| `AGENTS.md` | AI 工作规范、代码风格、项目约定 |
| `PROJECT_MEMORY.md` | 项目历史决策记录 |
| `CURRENT_TASK.md` | 当前任务进度 |

建议每次功能完成后更新 `PROJECT_MEMORY.md`，工作结束前更新 `CURRENT_TASK.md`，确保开新对话时 Claude 能无缝接上。
