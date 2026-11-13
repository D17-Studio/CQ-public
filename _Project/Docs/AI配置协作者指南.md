# AI Agent 配置指南

介绍如何把个人配置注入 agent 上下文，让 AI 每次启动时自动获得你的协作偏好、架构知识和当前进度。

## 配置说明

### 文件结构

```
_Project/AIConfig/
└── 你的名字/
    ├── PROJECT_MEMORY.md    ← 项目架构决策，跨会话记忆
    ├── AGENTS.md            ← AI 行为边界、编码偏好
    └── CURRENT_TASK.md      ← 当前在做的事、待办清单
```

### 各文件说明

#### PROJECT_MEMORY.md
项目架构决策备忘。AI 每次启动时加载，恢复对项目核心设计的理解。内容应包含：目录结构、依赖注入规范、关键架构决策、常用文件索引。

#### AGENTS.md
告诉 AI 怎么跟你协作。内容包括：编码风格偏好、注释规范、行为边界（比如"先讨论再写代码"）。

#### CURRENT_TASK.md
当前进度清单。包含：本轮在做什么、已完成 / 待办事项、上次做到哪了。每次大改后更新。

### 为什么放个人文件夹？

- **防止冲突**：每个人的 AI 配置（AGENTS、进度）完全不同，放一起必然产生 git 冲突
- **可参考不可干扰**：组员的配置在各自目录下，可以互相看、学习，但互不影响
- **注入配置也放本地**：每人的注入方式与路径自行维护，不随仓库共享

### 首次配置步骤

1. 在 `_Project/AIConfig/` 下新建以你名字命名的文件夹
2. 复制 `Aquila4222/` 中的三个文件作为模板，修改内容
3. 按下方「注入方式」一节配置你的 agent

### 维护

- `PROJECT_MEMORY.md` — 架构变更时更新
- `AGENTS.md` — 协作习惯调整时更新
- `CURRENT_TASK.md` — 每次会话结束时更新进度

## 注入方式

### Claude Code

项目根目录的 `CLAUDE.md` 会在新对话启动时自动加载，其中的 `@路径` 引用会被展开并注入上下文。AI 无需手动加载即可获得你的协作偏好、架构知识和当前进度。

示例内容：
```markdown
@_Project/AIConfig/Aquila4222/AGENTS.md
@_Project/AIConfig/Aquila4222/CURRENT_TASK.md
@_Project/AIConfig/Aquila4222/PROJECT_MEMORY.md
```

把路径中的 `Aquila4222` 换为你的文件夹名。`CLAUDE.md` 已被 `.gitignore`，每人本地自行维护，只写自己的引用路径（可附加少量项目通用说明）。

### DeepSeek Harness

DSH 原生只注入项目根目录的 `AGENTS.md`，其中的 `@路径` 引用不会展开。安装 `dsh-context-imports` 插件后，引用会像 Claude Code 一样展开并注入上下文。

在项目根目录的 `AGENTS.md` 中写入同样的引用（插件默认扫描 `AGENTS.md` 与 `CLAUDE.md`）：

```markdown
@_Project/AIConfig/Aquila4222/AGENTS.md
@_Project/AIConfig/Aquila4222/CURRENT_TASK.md
@_Project/AIConfig/Aquila4222/PROJECT_MEMORY.md
```

安装插件，之后重启 `dsh web`：

```sh
dsh plugin --profile web add dsh-context-imports
```

无需额外配置。根目录 `AGENTS.md` 同样应本地维护、不提交仓库，避免个人路径进 git。
