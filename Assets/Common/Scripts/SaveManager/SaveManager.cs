using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class SaveManager : MonoBehaviour
{
    #region 单例

    private static SaveManager _instance;
    public static SaveManager Instance => _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeRuntime()
    {
        GameObject obj = new GameObject("SaveManager");
        _instance = obj.AddComponent<SaveManager>();
        defaultSavingPath = Path.Combine(Application.persistentDataPath, "DefaultSavingPath");
        DontDestroyOnLoad(obj);
    }

    #endregion

    private static string defaultSavingPath;

    private readonly Dictionary<System.Type, TypeStorage> _saveDict = new();
    
    /// <summary>
    /// 存储特定类型的所有数据条目，以及该类型的可序列化标志，以及保存路径。
    /// </summary>
    private class TypeStorage
    {
        public Dictionary<string, DataEntry> Entries { get; }
        public bool IsSerializable { get; }
        
        public string Path { get; }

        public TypeStorage(Dictionary<string, DataEntry> entries , bool isSerializable , string path)
        {
            this.Entries = entries;
            this.IsSerializable = isSerializable;
            this.Path = path;
        }
        
        /// <summary>
        /// 单个存档数据条目，包含实际数据对象和脏标记。
        /// </summary>
        public class DataEntry
        {
            public object Save { get; }
            public bool IsDirty { get;set; }

            public DataEntry(object save, bool isDirty)
            {
                this.Save = save;
                this.IsDirty = isDirty;
            }
        }
    }
    
    /// <summary>
    /// 获取数据
    /// </summary>
    /// <param name="key">数据名</param>
    /// <typeparam name="T">类型</typeparam>
    /// <returns>类型</returns>
    public T Get<T>(string key) where T : class, new()
    {
        var entry = GetEntry<T>(key);
        var save = entry.Save;
        
        return (T)save;//二维字典已经按照类型隔离，所以save必定是类型T
    }

    /// <summary>
    /// 将数据设为脏
    /// </summary>
    /// <param name="key">数据名</param>
    /// <typeparam name="T">类型</typeparam>
    public void SetDirty<T>(string key) where T : class, new()
    {
        var entry = GetEntry<T>(key);
        entry.IsDirty = true;
    }

    /// <summary>
    /// 保存单个数据
    /// </summary>
    /// <param name="key">数据名</param>
    /// <typeparam name="T">类型</typeparam>
    public void Save<T>(string key) where T : class, new()
    {
        var storage = GetStorage<T>();
        var entry =  GetEntry<T>(key);
        
        string path = storage.Path;//存储文件夹路径
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        
        string content =  JsonUtility.ToJson(entry.Save);//获取类转换成的json文本
        string pathName = Path.Combine(path,key + ".json");//存储json文件名
        File.WriteAllText(pathName, content);
        entry.IsDirty = false;//清除脏标记
    }
    
    /// <summary>
    /// 保存所有脏数据
    /// </summary>
    public void SaveAllDirty()
    {
        SaveAll(true);
    }

    /// <summary>
    /// 强制保存所有数据
    /// </summary>
    public void SaveAllForce()
    {
        SaveAll(false);
    }
    
    private void OnApplicationQuit()
    {
        SaveAllForce();//退出时强制保存所有数据
    }

    //获取或创建容器
    private TypeStorage GetStorage<T>()
    {
        if (_saveDict.TryGetValue(typeof(T), out var value))
        {
            return value;
        }
        else
        {
            var dict = new Dictionary<string, TypeStorage.DataEntry>();
            bool serializable = typeof(T).IsSerializable;
            string path = Path.Combine(defaultSavingPath,typeof(T).Name);
            var storage = new TypeStorage(dict, serializable, path);
            _saveDict.Add(typeof(T),storage);
            return storage;
        }
    }

    //获取或创建数据条目
    private TypeStorage.DataEntry GetEntry<T>(string key) where T : class, new()
    {
        var storage = GetStorage<T>();
        
        if (storage.Entries.TryGetValue(key, out var value))
        {
            return value;
        }
        
        if (storage.IsSerializable == false)
            Debug.LogWarning($"需要加载的类型：[{typeof(T)}] 无[System.Serializable]特性，可能无法被序列化后存储！");
        
        
        T save =  new T();
      
        if (!Directory.Exists(storage.Path))
        {
            Directory.CreateDirectory(storage.Path);
        }
        
        string pathName = Path.Combine(storage.Path,key + ".json");
        if (File.Exists(pathName))
        {
            string json = File.ReadAllText(pathName);
            save = JsonUtility.FromJson<T>(json);
        }

        if (save == null)
        {
            Debug.LogWarning($"读取文件时发生错误！可能为文件损坏、权限、磁盘满等异常。即将使用默认数据。异常文件：{pathName}");
            //可加入异常处理
            save = new T();
        }
        
        var entry = new TypeStorage.DataEntry(save,false);
        storage.Entries.Add(key, entry);
        return entry;
    }
    
    private void SaveAll(bool doSaveDirtyOnly)
    {
        foreach (var typeStorage in _saveDict)//开始遍历第一层dictionary
        {
            string path = typeStorage.Value.Path;//存储文件夹路径
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            foreach (var entry in typeStorage.Value.Entries)//开始遍历第二层dictionary
            {
                if (doSaveDirtyOnly && entry.Value.IsDirty == false)//如果不保存脏数据则跳过非脏数据
                    continue;
                
                string content =  JsonUtility.ToJson(entry.Value.Save);//获取类转换成的json文本
                string pathName = Path.Combine(path,entry.Key + ".json");//存储json文件名
                File.WriteAllText(pathName, content);
                entry.Value.IsDirty = false;//清除脏标记
            }
        }
    }
}
