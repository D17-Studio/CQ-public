# ObjectPool 对象池

## 概述

泛型对象池，纯 C# 类，不依赖 Unity 生命周期。用于复用 `GameObject` 上的 `Component`，避免频繁 `Instantiate` / `Destroy` 带来的 GC 压力。

---

## 核心设计

- **泛型 `ObjectPool<T>`**：`T` 必须继承 `Component` 且实现 `IPoolable`
- **`Stack<T>` 存储**：`Get` / `Return` 均为 O(1)
- **回调归还**：对象通过 `IPoolable.SetReturnCallback` 获得归还入口，主动调用归还

---

## 接口

### IPoolable

```csharp
public interface IPoolable
{
    void SetReturnCallback(Action onReturn);
}
```

池在 `Get()` 时注入回调，对象完毕后调用 `onReturn()` 即可归还。

实现示例：

```csharp
public class NoteVE : MonoBehaviour, IPoolable
{
    private Action _onReturn;

    public void SetReturnCallback(Action onReturn)
    {
        _onReturn = onReturn;
    }

    public void ReturnToPool()
    {
        _onReturn?.Invoke();
    }
}
```

---

## ObjectPool\<T\>

### 构造

| 参数 | 说明 |
|------|------|
| `prefab` | 携带 `T` 组件的预制体 |
| `maxSize` | 池最大容量，默认 500 |

```csharp
var pool = new ObjectPool<NoteVE>(noteVEPrefab, maxSize: 300);
```

### 公开成员

| 成员 | 类型 | 说明 |
|------|------|------|
| `Get()` | 方法 | 从池中取一个对象（不自动激活） |
| `Return(T)` | 方法 | 归还对象到池（自动 SetActive(false)） |
| `WarmUp(int)` | 方法 | 预创建指定数量对象 |
| `Clear()` | 方法 | 清空池，销毁所有缓存对象 |
| `Count` | 属性 | 当前池中可用对象数 |
| `MaxSize` | 属性 | 池最大容量 |

### 行为说明

- **`Get()`**：池中有则取出，无则 `Instantiate(prefab)`。自动绑定归还回调。
- **`Return(T)`**：若池未满则 `SetActive(false)` 后入池，已满则 `Destroy`。
- **`WarmUp(int)`**：预创建对象，不会超出 `maxSize`。
- **`Clear()`**：销毁池内全部对象并清空。通常由 Manager 在场景卸载时调用。

---

## 使用方式

```csharp
// Manager 持有池
public class NoteVEManager : MonoBehaviour
{
    [SerializeField] private NoteVE _prefab;
    private ObjectPool<NoteVE> _pool;

    void Awake()
    {
        _pool = new ObjectPool<NoteVE>(_prefab, maxSize: 500);
        _pool.WarmUp(100);
    }

    public NoteVE Spawn(NoteData data)
    {
        var note = _pool.Get();
        note.Initialize(data);
        note.gameObject.SetActive(true);
        return note;
    }

    void OnDestroy()
    {
        _pool.Clear();
    }
}
```

```csharp
// 对象主动归还
public class NoteVE : MonoBehaviour, IPoolable
{
    private Action _onReturn;

    public void SetReturnCallback(Action onReturn) => _onReturn = onReturn;

    // 动画播完或 Note 超时后调用
    void OnAnimationComplete()
    {
        _onReturn?.Invoke();
    }
}
```

---

## 相关文件

- `IPoolable.cs` — 池化对象接口
- `ObjectPool.cs` — 泛型对象池实现
