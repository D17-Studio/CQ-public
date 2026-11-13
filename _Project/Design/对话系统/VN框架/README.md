# VN 对话系统（C# 核心 + HTML 可视化测试）

一个与引擎无关的 Visual Novel 运行时。C# 负责脚本解析、指令执行、变量条件、场景状态与存档；
HTML 只作为可视化测试的表现层，通过一个最小 Web 宿主与 C# 核心通信。

## 运行

需要 .NET 8 SDK。

```bash
cd _Project/Test/对话系统
dotnet run --project VnHost
```

打开 `http://127.0.0.1:5177`。

自动化自测（解析、分支、结局、选项条件、存档往返、错误用例）：

```bash
dotnet run --project VnHost -- --selftest
```

## 目录

```text
对话系统/
├─ VnCore/                纯 C# 运行时（netstandard2.1，无引擎依赖，可直接给 Unity 引用）
│  ├─ VnScriptParser.cs   文本 DSL -> 指令程序
│  ├─ VnMachine.cs        指令执行 / 场景 / 历史 / 存档
│  ├─ VnCondition.cs      变量、操作数、比较条件
│  ├─ VnInstruction.cs    指令定义
│  ├─ VnState.cs          行、选项、历史、快照 DTO
│  └─ ...
├─ VnHost/                最小 ASP.NET Core 宿主 + 静态页面
│  ├─ Program.cs          Minimal API
│  ├─ VnSession.cs        单会话封装与 API DTO
│  ├─ StoryAssets.cs      资产加载
│  ├─ SelfTest.cs         无浏览器自测
│  ├─ assets/
│  │  ├─ story.vns        剧情脚本
│  │  └─ characters.json  角色表
│  └─ wwwroot/            HTML 表现层
└─ README.md
```

## 架构

- `VnMachine` 同步执行指令，遇到 `台词 / 选项 / 结束` 时返回 `VnStepResult`，无异步、无引擎依赖。
- 表现层需要的全部状态（当前台词、场景、立绘、BGM、变量、历史、程序计数器）由核心导出。
- 存档是纯数据快照 `VnSnapshot`：程序计数器、调用栈、变量、场景、历史、当前行；
  宿主只做 JSON 序列化，浏览器只负责存取 localStorage。
- 宿主是单会话的测试服务器：一个页面、一份运行时，接口不面向多用户。

### HTTP 接口

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/bootstrap` | 标题、角色、标签列表等元信息 |
| POST | `/api/start` | 从头开始，可传 `{ "label": "标签" }` |
| POST | `/api/advance` | 推进到下一句台词 / 选项 / 结束 |
| POST | `/api/choose` | `{ "index": 0 }` 选择当前选项 |
| POST | `/api/jump` | `{ "label": "标签" }` 调试跳转 |
| POST | `/api/save` | 导出当前快照 |
| POST | `/api/load` | `{ "snapshot": ... }` 恢复快照 |

### 可视化测试页面

- 纯色、方角、无阴影/渐变/动画/模糊；没有打字机，核心返回的整句台词一次显示。
- 页面直接展示核心输出：`background` / `music` / `kind` / `pc`、角色（id / name / color / position / expression）、
  台词（speakerId / name / color / text）、选项、历史、变量、标签。
- 背景用场景 id 生成的纯色块表示，角色用方角文本框表示，说话人用粗边框与纯色底标识。
- 打开页面后自动调用 `/api/start`；点击对话框、空格或回车推进，数字键选择，Esc 关闭面板。
- 顶部「Core I/O」窗口按时间顺序显示每次请求的输入 JSON 与宿主返回的原始 JSON，便于核对核心输出；可清空。
- 「回放 / 存档 / 调试」分别对应 `history`、`snapshot`、`variables` 与 `labels`。

## 脚本语法

文件：`VnHost/assets/story.vns`，UTF-8。标签名、变量名支持中文；台词用英文冒号。
以「单词 + 英文冒号」开头且单词符合标识符规则的行会按台词解析，其余行是旁白；
正文需要出现英文冒号时，可改写语序或使用中文冒号。

```text
# 注释（整行）

@title 序章 · 放学后的音乐室

:: start
@bg 音乐室 · 黄昏
@bgm 钢琴 · 放学后
旁白直接写，不需要前缀。
akane: 有角色的台词写成「角色 id: 文本」。

@show akane center 微笑
@show kai left 倚墙
@hide kai

@set 好感 = 0
@set 好感 += 1

@if 好感 >= 2
akane: 好结局分支。
@elif 好感 >= 1
akane: 普通分支。
@else
akane: 遗憾分支。
@endif

@choice
* 选项一 -> label_a
* 选项二 -> label_b [if 好感 >= 1]

:: label_a
@call 公共插曲
@jump finale

:: finale
@jump ending

:: 公共插曲
旁白: 可复用的片段。
@return

:: ending
旁白: —— 结局
@end
```

### 指令表

| 指令 | 说明 |
| --- | --- |
| `:: 标签` / `@label 标签` | 定义跳转标签 |
| `@title 文本` | 脚本标题（只取最后一个） |
| `@bg 场景` | 切换背景 |
| `@bgm 曲目` / `@bgm stop` | 设置 / 停止 BGM |
| `@show 角色 [left|center|right] [表情]` | 显示或更新立绘，位置缺省为 center，表情缺省取角色表 |
| `@hide 角色` | 隐藏立绘 |
| `@set 变量 = 值` | 赋值；值可以是数字、true/false、双引号字符串或另一个变量 |
| `@set 变量 += 值` / `-=` | 数值增减；未定义变量按 0 处理 |
| `@if 条件` / `@elif` / `@else` / `@endif` | 条件块，可嵌套 |
| `@jump 标签` | 跳转 |
| `@call 标签` / `@return` | 子过程调用 / 返回 |
| `@choice` + `* 文本 -> 标签 [if 条件]` | 选项；条件不满足的选项不会显示 |
| `@end` | 结束故事 |
| `# 文本` | 整行注释 |

### 条件

- 运算符：`== != > >= < <=`
- 单操作数视为真值判断：数字非 0、布尔为 true、字符串非空即真。
- 未定义变量按 `0` 参与计算和比较。
- 字符串必须用双引号，例如 `@set 名字 = "茜"`、`@if 名字 == "茜"`。
- 数字和数字可以跨 int/float 比较；字符串只按序数比较；类型不匹配时只可能相等为 false。

### 角色表

`VnHost/assets/characters.json`：

```json
{
  "characters": [
    { "id": "akane", "name": "茜", "color": "#ff8fb1", "defaultExpression": "普通" }
  ]
}
```

`@show` 使用 `id`；台词里写 `akane: ...`，界面显示 `name` 与 `color`。

## 演示剧情

标题为「序章 · 放学后的音乐室」，包含：

- 三选一开场（其中一项带条件显示）；
- `@call` 公共合奏片段；
- 第二次选择与条件选项；
- 按 `好感` 分流三个结局（好 / 普通 / 遗憾）；
- 立绘显示、换位、表情、隐藏、背景与 BGM 切换、变量与跳转。

## 在 Unity 中复用

1. 把 `VnCore` 的源码或编译出的 `CQ.Vn.Core.dll` 引入 Unity（netstandard2.1，兼容 Unity 2021+）。
2. 用 `VnScriptParser.Parse(text)` 得到 `VnProgram`，用 `characters.json` 构造 `VnCharacterTable`。
3. 创建 `VnMachine`，调用 `Start / Advance / Choose`，把 `VnStepResult` 与场景状态交给自己的 UI。
4. 存档直接使用 `ExportSnapshot / ImportSnapshot`，由 Unity 侧负责序列化落盘。

HTML 宿主与 `wwwroot` 只是测试壳，不参与 Unity 构建。

## 当前限制

- 立绘、背景、BGM 都是占位表现：角色用方角文本块，背景用场景 id 生成的纯色，BGM 仅显示轨道名；无音频。
- 单会话宿主，不支持多用户同时访问。
- 没有自动播放、语音、快进、回滚（只有回放记录）；没有本地化与字库管理。
- 脚本没有内联注释，`#` 注释必须独占一行。
