# AI Agent 配置指南

项目使用 Claude Code / GitHub Copilot 等 AI IDE 时，可以通过 SessionStart hook 自动将个人配置注入 AI 上下文。

## 文件结构

```
_Project/AIConfig/
└── 你的名字/
    ├── PROJECT_MEMORY.md    ← 项目架构决策，跨会话记忆
    ├── AGENTS.md            ← AI 行为边界、编码偏好
    └── CURRENT_TASK.md      ← 当前在做的事、待办清单

.claude/
  └── settings.local.json   ← SessionStart hook，自动注入上面三个文件
```

## 各文件说明

### PROJECT_MEMORY.md
项目架构决策备忘。AI 每次启动时加载，恢复对项目核心设计的理解。内容应包含：目录结构、依赖注入规范、关键架构决策、常用文件索引。

### AGENTS.md
告诉 AI 怎么跟你协作。内容包括：编码风格偏好、注释规范、行为边界（比如"先讨论再写代码"）。

### CURRENT_TASK.md
当前进度清单。包含：本轮在做什么、已完成 / 待办事项、上次做到哪了。每次大改后更新。

### .claude/settings.local.json
SessionStart hook，在新对话启动时自动读取你个人目录下的三个 .md 文件，通过 `additionalContext` 注入到 AI 的 system prompt 中。AI 无需手动加载即可获得你的协作偏好、架构知识和当前进度。

示例 hook 配置：
```json
{
  "hooks": {
    "SessionStart": [
      {
        "matcher": "",
        "hooks": [
          {
            "type": "command",
            "command": "node -e \"const{readFileSync,existsSync}=require('fs');const{join}=require('path');const base=join(process.cwd(),'_Project','AIConfig','你的名字');const files=['AGENTS.md','CURRENT_TASK.md','PROJECT_MEMORY.md'];let ctx=[];for(const f of files){const fp=join(base,f);if(existsSync(fp)){ctx.push('--- '+f+' ---',readFileSync(fp,'utf-8'));}}console.log(JSON.stringify({hookSpecificOutput:{hookEventName:'SessionStart',additionalContext:ctx.join('\\n\\n')}}));\""
          }
        ]
      }
    ]
  }
}
```

### CLAUDE.md（根目录，可选）
项目级的公共指令，会被自动注入 system prompt。内容为项目通用的说明，不包含个人配置。已被 `.gitignore`，每人本地自行维护。

## 为什么放个人文件夹？

- **防止冲突**：每个人的 AI 配置（AGENTS、进度）完全不同，放一起必然产生 git 冲突
- **可参考不可干扰**：组员的配置在各自目录下，可以互相看、学习，但互不影响
- **Hook 也放本地**：`.claude/settings.local.json` 已被 `.gitignore`，每人维护自己的 hook 和路径

## 首次配置步骤

1. 在 `_Project/AIConfig/` 下新建以你名字命名的文件夹
2. 复制 `Aquila4222/` 中的三个文件作为模板，修改内容
3. 在项目根目录创建 `.claude/settings.local.json`，写入 SessionStart hook（参考上面示例，把路径中的 `Aquila4222` 换为你的文件夹名）
4. 启动 AI，新会话开始时 hook 会自动注入你的配置

## 维护

- `PROJECT_MEMORY.md` — 架构变更时更新
- `AGENTS.md` — 协作习惯调整时更新
- `CURRENT_TASK.md` — 每次会话结束时更新进度
- `.claude/settings.local.json` — 几乎不需要动
