# Unity项目Git使用规范

> AI写的，看着大致遵守以下就可以

---

## 📁 分支命名规范

分支命名采用 **`类型/描述`** 格式，全部小写，描述单词间使用短横线 `-` 连接。

| 类型 | 说明 | 示例 |
|------|------|------|
| `feature/` | 新功能开发 | `feature/weapon-system`、`feature/ui-inventory` |
| `bugfix/` | 修复bug（非紧急） | `bugfix/jump-physics`、`bugfix/inventory-save` |
| `hotfix/` | 线上紧急修复 | `hotfix/crash-on-startup`、`hotfix/payment-failed` |
| `release/` | 发布准备分支 | `release/v1.2.0`、`release/v2.0.0` |
| `refactor/` | 代码重构（不改变功能） | `refactor/player-controller` |
| `test/` | 试验性功能，用完即弃 | `test/new-shader-effect` |

**正确示例**：  
`feature/third-person-camera`  
`bugfix/ui-button-not-clickable`  
`hotfix/critical-login-error`

---

## 🌿 分支管理策略

采用 **简化版Git Flow**，适合小型团队快速迭代。
```
main（主干）
└── develop（开发集成分支）
├── feature/xxx（功能分支）
├── bugfix/xxx（修复分支）
└── release/vx.x.x（发布分支）
```
### 核心规则

| 分支 | 保护级别 | 生命周期 | 规则 |
|------|---------|---------|------|
| `main` | 受保护 | 永久 | 仅接受 `release/` 和 `hotfix/` 的合并。每次合并打Tag。 |
| `develop` | 受保护 | 永久 | 所有功能开发的集成分支。每日开始工作前必须拉取最新代码。 |
| `feature/*` | 普通 | 临时 | 从 `develop` 拉出，完成后合并回 `develop` 并删除。 |
| `bugfix/*` | 普通 | 临时 | 从 `develop` 拉出，完成后合并回 `develop` 并删除。 |
| `hotfix/*` | 普通 | 临时 | 从 `main` 拉出，完成后同时合并到 `main` 和 `develop`，并打Tag。 |
| `release/*` | 普通 | 临时 | 从 `develop` 拉出，只做测试和修复，完成后合并到 `main` 并打Tag，再同步回 `develop`。 |

### 🔔 Unity项目特别提醒

- **场景文件（`.unity`）和预制体（`.prefab`）极易冲突**：尽量避免多人同时修改同一场景/预制体。建议按功能拆分子场景，或明确分工。
- **Library文件夹**：必须加入 `.gitignore`，不要提交。每个成员自行生成。
- **大型资源文件**：考虑使用Git LFS管理纹理、音频、模型等二进制文件。

---

## 📝 Commit命名规范

采用 **`<类型>(<模块>): <简短描述>`** 格式，描述使用中文（团队统一）。

### 类型说明

| 类型 | 说明 |
|------|------|
| `feat` | 新增功能 |
| `fix` | 修复bug |
| `refactor` | 代码重构（不改变功能） |
| `style` | 格式调整（空格、缩进等，不影响逻辑） |
| `docs` | 文档更新 |
| `perf` | 性能优化 |
| `asset` | 资源文件变更（模型、贴图、音频等） |
| `scene` | 场景文件变更 |
| `config` | 配置文件变更（Input、Physics、ProjectSettings等） |

### 示例

```
feat(战斗): 添加武器切换功能
fix(UI): 修复背包界面关闭时闪退的问题
refactor(角色): 重构移动控制逻辑，降低耦合
asset(音效): 更新背景音乐和打击音效
scene(主城): 调整NPC摆放位置
config(输入): 新增手柄摇杆映射
```
### 注

建议按照改动拆分Commit，一个改动一个Commit

但是对于有大量改动并且不想拆的Commit，使用misc，示例：`misc(多项)：改动了XXX、XXX、XXX`