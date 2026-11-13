# RPG&VN 语法设计方案

## 基底

此 RPG&VN 语法基于常见VN语法，但是因为游戏所需加入了轻量的RPG元素，具体表现为：

1. 游戏中的角色具有“位置”状态
2. 在剧情的同一个阶段，玩家可以找不同的角色反复对话

## 实现效果

玩家平时可以自由切换场景，可以看到角色会待在不同的场景中，可以找他们对话。

发起对话后，就进入了 VN 模式，只能进行Advance/Choice等操作，无法自由移动。

但是比起RPG，玩家本身没有背包、武器、等级等状态。

总的来说，是一个可以在场景中找不同npc对话的 视觉小说(VN)

## 引入 Stage/Event/Dialogue

Stage 代表剧情状态的划分，用于维护一块剧情逻辑上紧密的Event/Dialogue，仅能内部跳转

~~~
# 以下为同义语法

== Stage1
@stage Stage1
~~~



Event 主要代表dialogue入口，仅外部能触发

~~~
# 以下为同义语法

!! 店长交互
@event 店长交互
~~~



Dialogue 类似 VN 的 label ，用于划分对话，同时表明“玩家进入了对话”的状态，仅内部能跳转

~~~
# 以下为同义语法

:: 店长让你找扳手
@dialogue 店长让你找扳手
@label 店长让你找扳手
~~~



此三者大致结构：

~~~
== Stage1

!! Event0
!! Event1
...

:: Dialogue0
:: Dialogue1
...

== Stage2
== Stage3
...
~~~



关于跳转：

- stage 只能通过jump
- 由 event 进入 dialogue 只能使用 call ，并且最终需要一个 return 退出对话
- dialogue 内部无论 jump/call 都可以



关于作用域：

- event/dialogue 仅在 stage 内部生效 （不过可以有全局 dialogue）
- dialogue 与 stage 也有同名冲突



关于 event 激活状态：

- event 可以被 enable/disable 

~~~
!! 扳手交互
@set 找到扳手 = 1
@disable 扳手交互
~~~



关于 stage 进行场景初始化：

- stage 可以紧跟指令，会在进入 stage 时执行：

~~~
== stage3
@show ...
@bgm ...
@disable 螺丝刀交互

!! 螺丝刀交互

:: 店长要你找螺丝刀
@enable 螺丝刀交互
~~~



关于 event 类型：

- 往往每个角色都会有一个交互 event，比如在绝大部分场景中都有 !!店长交互 ， 但是 event 内部不会和 character 绑定。

- 还有其他事件 event 比如 “!!打完一首歌” “!!游玩SongXXX的Ex难度并且仅接下所有Mute” “!!点击退出游戏”等



## show/bg 语义变化

@show 需要包含 角色、位置、差分、朝向，比如：

~~~
@show moka 阁楼床边 微笑 left
~~~

show会直接移动角色的舞台位置，并且之后会继续待在那边。



@bg 语义变化为对话中的玩家移动，比如：

~~~
@bg 吧台
~~~

这样只会改变玩家的场景，而不会带动角色移动



## jump/call 的变体：phase/loop/random

根据 RPG 的需要，防止维护大量变量与ifelse进行对话的选择。

使用phase，根据该call/jump指令调用的次数进行阶段式推进，最终始终跳转至随后一个阶段，并且可以加入次数参数

~~~
!! Event1
@call phase 对话一 对话二 对话三

!! Event2
@call phase 对话一:2 对话二:1 对话三
~~~

loop/random 一般只会在角色日常对话（也就是和剧本没什么关系）的时候出现，进行循环/随机跳转，可以加入次数或者权重

~~~
!! Event1
@call loop 对话一 对话二 对话三

!! Event2
@call loop 对话一:1 对话二:3 对话三:2

!! Event3
@call random 对话一 对话二 对话三

!! Event4
@call random 对话一:5 对话二:3 对话三:2
~~~

## task 指令

首先需要说明的是，@task 与 @bgm/show/hide 一样，都是维护内部的属性，外部不处理指令仅查询。

创建/修改 task：

~~~
# 同id为修改
@task 任务id 任务描述 1/10
@task find1 找到扳手 0/1
@task find1 找到扳手 100%/100%
~~~

删除/清空task

~~~
@task delete 任务id
@task clear
~~~

task 往往会和任务标志变量一起出现，但是他们并不绑定，比如：

~~~
== stage1

!! 店长交互
@if 找到扳手 = 0
@call 店长提示玩家找扳手
@else
@call 给店长交付扳手
@endif

!! 扳手交互
@disable 扳手交互
@task 找扳手 帮店长找到扳手 1/1

:: 店长提示玩家找扳手
......
@task 找扳手 帮店长找到扳手 0/1
@return

:: 给店长交付扳手
......
@task delete 找扳手
@jump stage2

== stage2
@task clear
~~~

## 完整概念演示

~~~
:: 店员日常对话1
@show 店员 - 微笑 -
店员: 要来点咖啡吗？
@show 店员 - 默认 -
@return

:: 店员日常对话2
@show 店员 - 微笑 -
店员: 工作辛苦了
@show 店员 - 默认 -
@return

== stage1
...
== stage23
@show 店长 阁楼卧室右 忙碌 right
@show 店员 咖啡厅吧台内中间 默认 right
@bgm 轻松主题
@disable 扳手交互

!! 店长交互
@if 发布任务 = 0
@call phase 店长找东西
@elif 找到扳手 = 0
@call 店长要玩家找扳手
@else
@call 向店长交付扳手
@endif

:: 店长找东西
@show 店长 - 疑惑 right
店长: 奇怪……扳手被我放到那里去了？
@show 店长 - 说话 left
店长: 啊，player，帮我找一下扳手吧
@show 店长 - 忙碌 right
@task find 帮助店长找到扳手 0/1
@set 发布任务 = 1
@show 扳手 后厨柜子里 默认 right
@enable 扳手交互
@return

:: 店长要玩家找扳手
@show 店长 - 说话 left
店长: 帮我找一下扳手吧
@show 店长 - 忙碌 right
@return

:: 向店长交付扳手
@show 店长 - 微笑 left
店长: 非常感谢
@task delete find
@jump stage24

!! 店员交互
@if 发布任务 = 0
@call loop 店员日常对话1 店员日常对话2
@elif 找到扳手 = 0
@call phase 店员帮助找扳手1 店员帮助找扳手2
@else
@call phase 店员让玩家把扳手给店长1 店员让玩家把扳手给店长2
@endif

:: 店员帮助找扳手1
@show 店员 - 思索 right
店员: 你问我店长吧扳手放哪里了？
店员: …………
@show 店员 - 说话 right
店员: 店长前几天来修了后厨的水管，可能随手放那边了吧
@show 店员 - 微笑 left
店员: 我带你去吧
@bg 后厨
@show 店员 后厨左 微笑 right
店员: 应该就在这里了，你找找吧
@show 店员 - 默认 -
@return

:: 店员帮助找扳手2
@show 店员 - 微笑 right
店员: 应该就在这里了
@show 店员 - 默认 -
@return

:: 店员让玩家把扳手给店长1
@show 店员 - 说话 right
店员: 这么快就找到了呀
@show 店员 - 微笑 right
店员: 那就麻烦你带给店长吧
@show 店员 - 默认 -
@return

:: 店员让玩家把扳手给店长2
@show 店员 - 微笑 right
店员: 麻烦你带给店长吧
@show 店员 - 默认 -
@return

!! 扳手交互
@call 找到扳手

:: 找到扳手
@show 扳手 screencenter 默认 right
找到扳手了
@hide 扳手
@disable 扳手交互
@set 找到扳手 = 1
@task find 帮助店长找到扳手 1/1
@return

== stage24
...
~~~















