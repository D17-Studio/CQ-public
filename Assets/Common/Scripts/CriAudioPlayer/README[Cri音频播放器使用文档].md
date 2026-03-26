# Cri音频播放器

本项目提供了一套基于 CriWare 的 Unity 音频管理工具，包含资源加载器 `CriLoader`、背景音乐播放器 `CriBgmPlayer` 和音效播放器 `CriSfxPlayer`，方便开发者快速集成和使用 CriWare 音频中间件。

---



# CriLoader

一个用于 CriWare 音频中间件的初始化与资源管理器，提供 ACF 注册和 CueSheet 加载的便捷接口。采用饿汉式单例，自动挂载并常驻场景。

---



## 初始化

该类为单例，在场景加载后自动创建实例，无需手动添加组件。但在使用其他功能前，需要先调用 `Initialize()` 挂载必要组件。

```csharp
// 在游戏启动时（例如第一个场景的 Awake/Start）调用
CriLoader.Instance.Initialize();
```

> `Initialize()` 会在当前 GameObject 上添加 `CriWareInitializer`、`CriAtom` 和 `CriWareErrorHandler` 三个组件。

---



## 资源存放路径

所有 CRware 音频资源（`.acf`、`.acb`、`.awb` 文件）**必须**放置在 `Assets/StreamingAssets` 文件夹下。

---



## 方法分类（可以不用看）

### ACF 注册

| 方法              | 参数                 | 说明                                                  |
| ----------------- | -------------------- | ----------------------------------------------------- |
| `RegisterAcf`     | `string acfFileName` | 手动注册指定的 ACF 文件（路径相对于 StreamingAssets） |
| `AutoRegisterAcf` | 无                   | 自动查找 StreamingAssets 下的第一个 `.acf` 文件并注册 |

### CueSheet 管理

| 方法               | 参数                                                         | 说明                                                         |
| ------------------ | ------------------------------------------------------------ | ------------------------------------------------------------ |
| `AddCueSheet`      | `string cueSheetName, string acbFileName, string awbFileName = null` | 手动添加 CueSheet，支持指定 ACB 和可选的 AWB 文件（路径均相对于 StreamingAssets） |
| `RemoveCueSheet`   | `string cueSheetName`                                        | 按名称移除已加载的 CueSheet                                  |
| `AutoAddCueSheets` | 无                                                           | 自动查找 StreamingAssets 下的所有 `.acb` 文件，并以文件名作为 CueSheet 名称添加；若存在同名 `.awb` 则一并加载 |

---



## 自动加载（重点）

推荐使用本类的自动加载方法，可以一键完成 ACF 注册和所有 CueSheet 的加载，简化初始化流程。

```csharp
// 在调用 Initialize() 之后
CriLoader.Instance.AutoRegisterAcf();   // 自动注册第一个 .acf 文件
CriLoader.Instance.AutoAddCueSheets();  // 自动添加所有 .acb 文件作为 CueSheet
```

**注意事项**：

- 自动加载时，**CueSheet 的名称取自 `.acb` 文件的文件名（不含扩展名）**。  
  例如 `BGM.acb` 会生成名为 `"BGM"` 的 CueSheet，后续的 `BGMPlayer` 或 `SEPlayer` 需要传入这个名称来加载对应的 CueSheet。
- 自动注册 ACF 时，只会注册搜索到的第一个 `.acf` 文件，如果项目中有多个 ACF，请手动使用 `RegisterAcf` 指定正确的文件。
- 自动添加 CueSheet 时，若存在与 `.acb` 同名的 `.awb` 文件，会自动一并加载。

---



## 注意事项

1. **初始化顺序**：必须在调用任何注册/加载方法前先执行 `Initialize()`，以确保底层组件已创建。
2. **路径约定**：所有文件均需放置在 `Assets/StreamingAssets` 下，调用时只需提供相对于该目录的文件名。
4. **ACF 与 CueSheet 依赖**：通常需要先注册 ACF，再添加 CueSheet（某些场景下 ACF 可省略，但推荐先注册）。
5. **单例生命周期**：实例在场景加载后自动创建，且不会被销毁，适合在游戏全程使用。

---



## 使用示例

在游戏启动时（例如第一个场景的 `Start` 方法中），推荐按以下方式初始化：

```csharp
using UnityEngine;

public class GameController : MonoBehaviour
{
    private void InitializeCriAudioPlayer()
    {
        // 1. 挂载必要的 CRI 组件
        CriLoader.Instance.Initialize();
        // 2. 自动注册 StreamingAssets 下的第一个 .acf 文件
        CriLoader.Instance.AutoRegisterAcf();
        // 3. 自动添加所有 .acb 文件作为 CueSheet
        CriLoader.Instance.AutoAddCueSheets();
    }

    void Start()
    {
        InitializeCriAudioPlayer();
    }
}
```



# CriBgmPlayer

一个基于 CriWare 的 BGM 播放器封装，提供便捷的音频播放控制，支持时间定位、速度调节、淡入淡出、循环播放等功能。

---

## 初始化

在使用前需要先创建实例。构造函数会自动调用 `Initialize()`，无需手动执行。

```csharp
private CriBgmPlayer bgmPlayer;

void Start()
{
    bgmPlayer = new CriBgmPlayer(); // 自动完成初始化
}
```

> `Initialize()` 会创建底层播放器、附加时间拉伸语音池、设置淡入淡出时间为 0，并自动订阅退出事件以便释放资源。

---

## 方法分类

### 加载音频资源

| 方法          | 参数          | 说明                                        |
| ------------- | ------------- | ------------------------------------------- |
| `SetCueSheet` | `string name` | 加载指定名称的 CueSheet，后续播放基于此资源 |

---

### 播放控制

| 方法         | 参数                              | 说明                             |
| ------------ | --------------------------------- | -------------------------------- |
| `Play`       | `int cueId = 0`                   | 按 Cue ID 播放，从开头开始       |
| `Play`       | `string name`                     | 按 Cue 名称播放，从开头开始      |
| `PlayAtTime` | `long startTimeMs, int cueId = 0` | 从指定毫秒位置开始播放（按 ID）  |
| `PlayAtTime` | `float startTime, int cueId = 0`  | 从指定秒位置开始播放（按 ID）    |
| `PlayAtTime` | `long startTimeMs, string name`   | 从指定毫秒位置开始播放（按名称） |
| `PlayAtTime` | `float startTime, string name`    | 从指定秒位置开始播放（按名称）   |
| `Stop`       | 无                                | 停止当前播放                     |
| `Pause`      | 无                                | 暂停当前播放                     |
| `Resume`     | 无                                | 恢复暂停的播放                   |

---

### 查询信息

| 方法                | 参数                   | 说明                                                         |
| ------------------- | ---------------------- | ------------------------------------------------------------ |
| `GetCurrentTime<T>` | 泛型 `T`               | 获取当前播放时间，`T` 为 `long` 返回毫秒，`float` 返回秒；失败返回 -1 |
| `GetCurrentTime`    | 无                     | 获取当前播放时间，默认返回秒（`float`）                      |
| `GetCueLength<T>`   | `int cueId = 0` (泛型) | 获取指定 Cue ID 的音频总时长，`T` 为 `long` 返回毫秒，`float` 返回秒；失败返回 -1 |
| `GetCueLength`      | `int cueId = 0`        | 获取指定 Cue ID 的音频总时长，默认返回秒                     |
| `GetCueLength<T>`   | `string name` (泛型)   | 获取指定 Cue 名称的音频总时长，`T` 为 `long` 返回毫秒，`float` 返回秒；失败返回 -1 |
| `GetCueLength`      | `string name`          | 获取指定 Cue 名称的音频总时长，默认返回秒                    |

---

### 音量控制

| 方法        | 参数           | 说明                                         |
| ----------- | -------------- | -------------------------------------------- |
| `SetVolume` | `float volume` | 设置音量，范围 0.0（静音）～ 1.0（原始音量） |

---

### 循环与淡入淡出

| 方法             | 参数        | 说明                                         |
| ---------------- | ----------- | -------------------------------------------- |
| `SetLoop`        | `bool loop` | 设置是否循环播放（对无内置循环点的波形有效） |
| `SetFadeInTime`  | `int ms`    | 设置淡入时间（毫秒）                         |
| `SetFadeInTime`  | `float s`   | 设置淡入时间（秒）                           |
| `SetFadeOutTime` | `int ms`    | 设置淡出时间（毫秒）                         |
| `SetFadeOutTime` | `float s`   | 设置淡出时间（秒）                           |

---

### 速度控制

| 方法       | 参数               | 说明                                                         |
| ---------- | ------------------ | ------------------------------------------------------------ |
| `SetSpeed` | `float speedRatio` | 设置播放速度，例如 `2.0f` 为 2 倍速，`0.5f` 为半速（通过时间拉伸实现，音调不变） |

---

### 生命周期管理

| 方法         | 说明                                                         |
| ------------ | ------------------------------------------------------------ |
| `Initialize` | 创建底层播放器、附加时间拉伸语音池、设置淡入淡出时间为 0，并自动订阅退出事件。**构造函数已自动调用，通常无需手动执行。** |
| `Dispose`    | 手动释放非托管资源，并取消退出事件的订阅。**应用退出时会自动调用，但也可在需要时提前手动释放。** |

---

## 注意事项

1. **单实例单播放**：每个 `CriBgmPlayer` 实例同时只能播放一个 BGM（调用新播放时会自动停止当前播放）。
2. **初始化顺序**：必须先调用 `Initialize()` 再使用其他方法。
3. **CueSheet 加载**：调用任何播放或查询方法前，需先通过 `SetCueSheet()` 加载音频资源。
4. **时间拉伸语音池**：类内部已创建支持时间拉伸的专用语音池，无需额外配置。
5. **资源释放**：程序退出时会自动释放非托管资源，无需手动调用 `Dispose()`。
6. **⚠️ 后台挂起**：游戏在后台挂起时，播放器**不会自动暂停**，如有需要请**务必**手动调用 `Pause()` 和 `Resume()` 方法。

---

## 使用示例

如果你的项目将每个 BGM 打包为单独的 CueSheet，则在播放不同 BGM 时需要先切换到对应的 CueSheet。

```csharp
using UnityEngine;

public class BGMController : MonoBehaviour
{
    private CriBgmPlayer bgmPlayer;
	
    //初始化，最好由GameController统一管理顺序，防止在CriLoader加载Cuesheet之前发生
    public void Initialize()
    {
        bgmPlayer = new CriBgmPlayer();
        // 加载 CueSheet
        bgmPlayer.SetCueSheet("BGM");
    }
	
    // 播放当前 CueSheet 中的默认 BGM
    public void PlayBGM()
    {
        bgmPlayer.Play();   // 使用 Cue ID 0 或默认 Cue
    }
	
    // 切换到另一个 BGM（对应另一个 CueSheet）并播放
    public void SwitchBGM()
    {
        // ⚠️ 重要：必须先加载目标 BGM 对应的 CueSheet
        bgmPlayer.SetCueSheet("BGM2");
        bgmPlayer.Play();
    }
}
```



# CriSfxPlayer

一个基于 CriWare 的 SFX（音效）播放器封装，提供便捷的音效播放控制，支持同时播放多个音效。

---

## 初始化

在使用前需要先创建实例。构造函数会自动调用 `Initialize()`，无需手动执行。

```csharp
private CriSfxPlayer sfxPlayer;

void Start()
{
    sfxPlayer = new CriSfxPlayer(); // 自动完成初始化
}
```

> `Initialize()` 会创建底层播放器，并自动订阅退出事件以便释放资源。

---

## 方法分类

### 加载音频资源

| 方法          | 参数          | 说明                                        |
| ------------- | ------------- | ------------------------------------------- |
| `SetCueSheet` | `string name` | 加载指定名称的 CueSheet，后续播放基于此资源 |

---

### 播放控制

| 方法        | 参数                                 | 说明                                                         |
| ----------- | ------------------------------------ | ------------------------------------------------------------ |
| `Play`      | `int cueId = 0, float volume = 1.0f` | 按 Cue ID 播放音效，可指定独立音量（与全局音量乘算），支持同时播放多个 |
| `Play`      | `string name, float volume = 1.0f`   | 按 Cue 名称播放音效，可指定独立音量（与全局音量乘算），支持同时播放多个 |
| `StopAll`   | 无                                   | 停止所有正在播放的音效                                       |
| `PauseAll`  | 无                                   | 暂停所有正在播放的音效                                       |
| `ResumeAll` | 无                                   | 恢复所有被暂停的音效                                         |

---

### 音量控制

| 方法        | 参数           | 说明                                                         |
| ----------- | -------------- | ------------------------------------------------------------ |
| `SetVolume` | `float volume` | 设置全局音量系数（范围 0.0 ~ 1.0），只影响后续播放的音效，已播放的音效不受影响。最终音量 = 全局音量系数 × 播放时指定的独立音量。 |

---

### 生命周期管理

| 方法         | 说明                                                         |
| ------------ | ------------------------------------------------------------ |
| `Initialize` | 创建底层播放器，并订阅退出事件。**构造函数已自动调用，通常无需手动执行。** |
| `Dispose`    | 手动释放非托管资源，并取消退出事件的订阅。**应用退出时会自动调用，但也可在需要时提前手动释放。** |

---

## 注意事项

1. **同时播放多个音效**：`CriSfxPlayer` 使用同一个 `CriAtomExPlayer` 实例，但每个 `Play()` 调用都会独立分配 Voice，因此支持同时播放多个音效。
2. **初始化顺序**：必须先调用 `Initialize()` 再使用其他方法。
3. **CueSheet 加载**：调用任何播放或查询方法前，需先通过 `SetCueSheet()` 加载音频资源。
4. **资源释放**：程序退出时会自动释放非托管资源，无需手动调用 `Dispose()`。
5. **⚠️ 后台挂起**：游戏在后台挂起时，播放器**不会自动暂停**，如有需要请**务必**手动调用 `Pause()` 和 `Resume()` 方法。

---

## 使用示例

通常情况下，所有音效会打包在同一个 CueSheet 中，因此只需要一个 `CriSfxPlayer` 实例即可管理全部音效，无需频繁切换 CueSheet。

```csharp
using UnityEngine;

public class SFXController : MonoBehaviour
{
    private CriSfxPlayer sfxPlayer;

    //初始化，最好由GameController统一管理顺序，防止在CriLoader加载Cuesheet之前发生
    public void Initialize()
    {
        // 创建播放器实例（构造函数自动完成初始化）
        sfxPlayer = new CriSfxPlayer();
        // 加载包含所有音效的 CueSheet（只需一次）
        sfxPlayer.SetCueSheet("SFX");
    }

    // 播放“击中”音效
    public void PlayHitSound()
    {
        // 使用 Cue 名称播放（名称在 CriAtom Craft 中创建 Cue 时指定）
        sfxPlayer.Play("Hit");
    }

    // 播放“冲刺”音效
    public void PlayDashSound()
    {
        sfxPlayer.Play("Dash");
    }

    // 播放自定义音效，并单独指定音量（可选）
    public void PlayCustomSound(string cueName, float volume = 1.0f)
    {
        sfxPlayer.Play(cueName, volume);
    }
}
```