using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// 分数存档（纯 C#，DI 绑定跨场景单例）。哑存储：传什么记什么，取优 / FC 规则在调用方（结算流程）。
/// 单文件持久化：Base64 payload + HMAC 签名，加载先验签后解析（不存在 = 空档，坏档 = 备份 + 空档）；写入即落盘（原子写）。
/// </summary>
public class ScoreArchive
{
    private const int CurrentVersion = 1;

    private readonly string _filePath;
    private readonly Dictionary<string, ScoreRecord> _records = new();

    /// <summary>构造即加载；之后查询全部走内存。</summary>
    public ScoreArchive(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    /// <summary>查询某谱面的最佳记录；没有记录时返回 Exists=false 的空视图。</summary>
    public ScoreView GetBest(string key)
    {
        if (_records.TryGetValue(key, out var record))
            return new ScoreView(true, record.BestScore, record.IsFullCombo);

        return default;
    }

    /// <summary>写入分数（传什么记什么）。写入即落盘。</summary>
    public void RecordScore(string key, int score)
    {
        GetOrCreate(key).BestScore = score;
        Save();
    }

    /// <summary>写入全连成就状态（传什么记什么）。写入即落盘。</summary>
    public void RecordFullCombo(string key, bool isFullCombo)
    {
        GetOrCreate(key).IsFullCombo = isFullCombo;
        Save();
    }

    private ScoreRecord GetOrCreate(string key)
    {
        if (_records.TryGetValue(key, out var record))
            return record;

        record = new ScoreRecord { Key = key };
        _records.Add(key, record);
        return record;
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
            return; // 首次运行：空档起步

        try
        {
            // 解壳 → 验签（Mac 须与 Payload 原文匹配）→ 解码解析
            var shell = JsonUtility.FromJson<ScoreFileShell>(File.ReadAllText(_filePath));
            if (shell == null || string.IsNullOrEmpty(shell.Payload) || shell.Mac != Sign(shell.Payload))
                throw new InvalidDataException("外壳为空或签名校验失败");

            string payloadJson = Encoding.UTF8.GetString(Convert.FromBase64String(shell.Payload));
            var data = JsonUtility.FromJson<ScoreArchiveData>(payloadJson);
            if (data?.Records == null)
                return;

            foreach (var record in data.Records)
            {
                // 跳过 Key 为空的脏条目
                if (record == null || string.IsNullOrEmpty(record.Key))
                    continue;
                _records[record.Key] = record;
            }

            Debug.Log($"[ScoreArchive] 已加载 {_records.Count} 条成绩记录：{_filePath}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ScoreArchive] 存档损坏或验签失败，已备份留证并重置为空档：{_filePath}\n{e.Message}");
            _records.Clear();
            BackupBrokenFile();
        }
    }

    private void Save()
    {
        try
        {
            var data = new ScoreArchiveData
            {
                Version = CurrentVersion,
                Records = new List<ScoreRecord>(_records.Values),
            };

            // Payload = Base64(数据 JSON)；Mac = 对 Payload 原文签名
            string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonUtility.ToJson(data)));
            string shellJson = JsonUtility.ToJson(new ScoreFileShell { Payload = payload, Mac = Sign(payload) });

            string tmpPath = _filePath + ".tmp";
            File.WriteAllText(tmpPath, shellJson);

            // 目标不存在（首次写）时 Replace 不可用，走 Move
            if (File.Exists(_filePath))
                File.Replace(tmpPath, _filePath, null);
            else
                File.Move(tmpPath, _filePath);
        }
        catch (Exception e)
        {
            // 内存数据仍正确：下次成功写入会全量序列化补写
            Debug.LogError($"[ScoreArchive] 写盘失败（内存数据仍正确，下次写入自动补写）：{_filePath}\n{e.Message}");
        }
    }

    private void BackupBrokenFile()
    {
        try
        {
            // 原档复制为 .bak 后移除，以空档继续
            File.Copy(_filePath, _filePath + ".bak", true);
            File.Delete(_filePath);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ScoreArchive] 坏档备份失败（继续以空档运行）：{e.Message}");
        }
    }

    private static string Sign(string payload)
    {
        // HMAC-SHA256(密钥, 原文)；原文 = 未解码的 payload 字符串
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(ScoreArchiveKey.Secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }
}

/// <summary>分数存档的磁盘外壳（Payload 以原文参与验签）。</summary>
[Serializable]
internal class ScoreFileShell
{
    public string Payload;
    public string Mac;
}
