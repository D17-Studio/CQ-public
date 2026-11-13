# SaveManager 使用文档

## 简介

SaveManager 是一个轻量级、易用的 Unity 游戏存档管理工具。它基于**类型隔离**、**懒加载**和**脏标记**机制，让您只需几行代码就能完成数据的持久化，无需关心文件读写细节。

## 主要特性

- **懒加载**：数据在首次访问时自动从磁盘加载或创建新实例，**不需要手动加载**
- **自动保存**：游戏退出时**自动保存**，防止数据丢失
- **脏标记**：修改数据后标记为“脏”，保存时仅写入修改过的数据，提升性能

## 快速开始

### 1. 定义数据类

所有要保存的类必须标记 `[System.Serializable]`。

```c#
[System.Serializable]
public class PlayerData
{
    public string playerName;
    public int level;
    public int gold;
}
```

### 2. 获取数据

使用`Get<T>(string key)`方法获取数据实例：

```c#
var data = SaveManager.Instance.Get<PlayerData>("player1");
```

- 传入的string为存档名，类名`PlayerData`和存档名`player1`能确定唯一的数据实例

获取时可能有如下情况：

- 若数据已缓存，直接返回
- 若未加载，则从磁盘读取文件，并返回该数据实例
- 若未加载且文件不存在，则创建新实例并返回

若数据类是**用户设置**之类的唯一数据，则可以使用：

```C#
var data = SaveManager.Instance.Get<SettingData>();
```

这样会默认将类名设置为存档名。

### 3. 修改数据并标记为脏

```c#
data.gold += 100;
SaveManager.Instance.SetDirty<PlayerData>("player1");
```

**注意**：修改数据如未 `SetDirty`，手动保存脏数据时会跳过。

### 4. 保存数据

```c#
// 只保存所有脏数据（推荐）
SaveManager.Instance.SaveAllDirty();

// 立即保存单个数据
SaveManager.Instance.Save<PlayerData>("player1");

// 强制保存所有数据（包括未修改的）
SaveManager.Instance.SaveAllForce();
```

或者您可以不使用任何的手动保存与脏标记，游戏退出时的**自动保存**绝大部分情况下够用。

## API 参考

| 方法                           | 说明                                         |
| :----------------------------- | :------------------------------------------- |
| `T Get<T>(string key)`         | 获取指定键的数据，不存在则从磁盘加载或新建。 |
| `void SetDirty<T>(string key)` | 将指定键的数据标记为脏（表示已修改）。       |
| `void Save<T>(string key)`     | 立即保存该键的数据到磁盘，并清除脏标记。     |
| `void SaveAllDirty()`          | 保存所有脏数据。                             |
| `void SaveAllForce()`          | 强制保存所有缓存的数据（忽略脏标记）。       |

- **泛型约束**：`T` 必须是 `class`，且拥有无参构造函数（`new()`）。

## 工作原理

### 数据结构

根据**类名**与**存档名**进行两次索引：

- 外层：`Dictionary<Type, TypeStorage>`，按数据类型分类
- 内层：`Dictionary<string, DataEntry>`，存储具体键值及脏标记

### 文件组织

存档文件保存在：
`Application.persistentDataPath/DefaultSavingPath/类型名/key.json`

例如：

- `PlayerData` → `.../DefaultSavingPath/PlayerData/player.json`
- `GameSettings` → `.../DefaultSavingPath/GameSettings/settings.json`

### 脏标记生命周期

1. 数据加载/创建时，脏标记为 `false`
2. 调用 `SetDirty` 后，标记为 `true`
3. 保存脏数据（`SaveAllDirty` 或 `Save<T>`）后，自动重置为 `false`
4. `SaveAllForce` 会保存所有数据并重置所有脏标记

## 注意事项

1. **数据类必须标记 `[System.Serializable]`**
   否则 `JsonUtility` 无法正确序列化，保存时只会生成空文件 `{}`，且控制台会输出警告。
5. **异常处理**
   文件读写未添加 try-catch，极端情况（磁盘满、权限不足）可能导致异常。

## 完整示例

```C#
using UnityEngine;

public class GameManager : MonoBehaviour
{
    private PlayerData player;

    void Start()
    {
        //游戏开始时从SaveManager获取实例
        player = SaveManager.Instance.Get<PlayerData>("player");
        Debug.Log($"欢迎回来，{player.playerName}！等级 {player.level}，金币 {player.gold}");
    }

    public void AddGold(int amount)
    {
        player.gold += amount;
        SaveManager.Instance.SetDirty<PlayerData>("player");
    }

    public void SaveGame()
    {
        SaveManager.Instance.SaveAllDirty();
        Debug.Log("游戏已保存");
    }
}
```