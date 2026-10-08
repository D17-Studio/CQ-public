# sync-public.sh 使用说明

从私有库同步代码到公开库 [CQ-public](https://github.com/Aquila4222/CQ-public)，自动排除美术/音频/闭源 SDK。

## 目录结构

```
D:/GitHub/
├── CQ-MusicGame/      ← 私有库（日常开发）
│   └── _Project/sync-public/
│       └── sync-public.sh
└── CQ-public/         ← 公开库（脚本自动维护）
```

## 用法

Git Bash 里运行：

```bash
/d/GitHub/CQ-MusicGame/_Project/sync-public/sync-public.sh
```

脚本会：
1. 显示私有库当前 commit，确认后执行
2. 清空公开库的 `Assets/` 和 `_Project/`
3. 从私有库全量复制
4. 删除不应公开的文件（贴图、音频、模型、字体、视频、原生库、闭源 SDK），并清理残留的 `.meta`
5. 确认后 `git add -A && git commit`

最后手动推送：

```bash
cd /d/GitHub/CQ-public
git push origin main
```

## 排除规则

| 类别 | 内容 |
|------|------|
| 贴图纹理 | png jpg psd tga bmp gif exr hdr ico 等 |
| 音频 | wav mp3 ogg aif flac m4a acb awb bank bnk wem 等 |
| 3D 模型 | fbx obj blend dae max ma mb usd 等 |
| 字体 | ttf otf ttc dfont |
| 视频 | mp4 mov avi webm mkv wmv |
| 二进制/原生库 | dll pdb exe so dylib chm |
| Mac/iOS 插件包 | `*.bundle` `*.dSYM` `*.xcframework`（目录形式） |
| 闭源 SDK | `Assets/Wwise/` `Assets/ThirdParty/CRIMW/` `Assets/ThirdParty/FMOD/` |
| Unity 内置 | `Assets/TextMesh Pro/` |
| Wwise 配置 | `Assets/WwiseSettings.xml`（含本机安装路径） |
| FMOD 工程 | `_Project/FMOD_Projects/` |

> 注意：同步范围只有 `Assets/` 和 `_Project/`。仓库根目录下的 `CQ-MusicGame_WwiseProject/`、`Packages/`、`ProjectSettings/` 等不会进入公开库，只保留在私有库。

## 新增闭源 SDK

目录加到 `EXCLUDED_DIRS`，单个文件加到 `EXCLUDED_FILES`：

```bash
EXCLUDED_DIRS=(
    "Assets/Wwise"
    "Assets/ThirdParty/CRIMW"
    "Assets/ThirdParty/FMOD"
    "Assets/ThirdParty/新SDK名"    # ← 加这行
    ...
)
```
