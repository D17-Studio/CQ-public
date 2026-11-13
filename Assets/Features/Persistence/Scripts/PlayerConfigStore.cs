using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 玩家配置存档（纯 C#，DI 绑定跨场景单例）：内存即时生效、检查点落盘（Save() / 应用退出兜底）。
/// 明文 JSON 单文件；缺档 = 默认值，坏档 = 备份留证 + 默认值。
/// </summary>
public class PlayerConfigStore
{
    private const int CurrentVersion = 1;

    private readonly string _filePath;
    private readonly PlayerConfig _config;
    private bool _dirty;

    /// <summary>构造即加载；之后读写全部走内存。</summary>
    public PlayerConfigStore(string filePath)
    {
        _filePath = filePath;
        _config = Load();
        Application.quitting += Save;
    }

    /// <summary>玩家全局流速。</summary>
    public float ScrollSpeed
    {
        get => _config.ScrollSpeed;
        set
        {
            if (_config.ScrollSpeed.Equals(value)) return;
            _config.ScrollSpeed = value;
            _dirty = true;
        }
    }

    /// <summary>输入偏移（毫秒，可正可负）。</summary>
    public int InputOffsetMs
    {
        get => _config.InputOffsetMs;
        set
        {
            if (_config.InputOffsetMs == value) return;
            _config.InputOffsetMs = value;
            _dirty = true;
        }
    }

    /// <summary>检查点落盘：脏才写，写失败保脏等下次重试。</summary>
    public void Save()
    {
        if (!_dirty) return;

        try
        {
            _config.Version = CurrentVersion;
            string tmpPath = _filePath + ".tmp";
            File.WriteAllText(tmpPath, JsonUtility.ToJson(_config));

            // 目标不存在（首次写）时 Replace 不可用，走 Move
            if (File.Exists(_filePath))
                File.Replace(tmpPath, _filePath, null);
            else
                File.Move(tmpPath, _filePath);

            _dirty = false;
        }
        catch (Exception e)
        {
            // 保脏：下次 Save 全量重写补上
            Debug.LogError($"[PlayerConfigStore] 写盘失败（内存值仍正确，下次保存重试）：{_filePath}\n{e.Message}");
        }
    }

    private PlayerConfig Load()
    {
        if (!File.Exists(_filePath))
            return new PlayerConfig(); // 首次运行：默认值起步

        try
        {
            var config = JsonUtility.FromJson<PlayerConfig>(File.ReadAllText(_filePath));
            if (config == null)
                throw new InvalidDataException("解析结果为空");
            
            Debug.Log($"[PlayerConfigStore] 已加载玩家配置：{_filePath}");
            return config;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PlayerConfigStore] 配置损坏，已备份留证并回默认值：{_filePath}\n{e.Message}");
            BackupBrokenFile();
            return new PlayerConfig();
        }
    }

    private void BackupBrokenFile()
    {
        try
        {
            // 原档复制为 .bak 后移除，以默认值继续
            File.Copy(_filePath, _filePath + ".bak", true);
            File.Delete(_filePath);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PlayerConfigStore] 坏档备份失败（继续以默认值运行）：{e.Message}");
        }
    }
}
