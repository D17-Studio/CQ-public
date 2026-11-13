using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CQMusicGame.Shared;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 扫描外部自制谱包目录，把 TrackInfo.txt 与谱面 / 音频 / 封面转换为运行时标准模型。
/// </summary>
public static class CustomTrackLoader
{
    private const string TrackInfoFileName = "TrackInfo.txt";

    /// <summary>
    /// 扫描根目录下的所有谱包子目录（按目录名排序）；单个谱包失败只警告并跳过。
    /// </summary>
    public static List<TrackData> LoadAll(string rootPath)
    {
        var tracks = new List<TrackData>();
        if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return tracks;

        var packDirs = Directory.GetDirectories(rootPath);
        Array.Sort(packDirs, StringComparer.OrdinalIgnoreCase);

        foreach (var packDir in packDirs)
        {
            try
            {
                var track = LoadPack(packDir);
                if (track == null) continue;

                tracks.Add(track);
                Debug.Log($"[CustomTrackLoader] 已加载自制谱：{track.MusicTitle}（{Path.GetFileName(packDir)}）");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CustomTrackLoader] 跳过谱包 {Path.GetFileName(packDir)}：{e.Message}");
            }
        }

        return tracks;
    }

    private static TrackData LoadPack(string packDir)
    {
        string packName = Path.GetFileName(packDir);
        string infoPath = Path.Combine(packDir, TrackInfoFileName);
        if (!File.Exists(infoPath))
        {
            Debug.LogWarning($"[CustomTrackLoader] 跳过谱包 {packName}：缺少 {TrackInfoFileName}");
            return null;
        }

        var info = TrackInfoSerializer.Parse(File.ReadAllText(infoPath));

        string musicPath = ResolvePackFile(packDir, info.MusicPath);
        if (musicPath == null || !File.Exists(musicPath))
        {
            Debug.LogWarning($"[CustomTrackLoader] 跳过谱包 {packName}：音频文件不存在（{info.MusicPath}）");
            return null;
        }

        if (!TryReadWavDuration(musicPath, out int durationMs))
        {
            Debug.LogWarning($"[CustomTrackLoader] 跳过谱包 {packName}：仅支持 PCM WAV（{info.MusicPath}）");
            return null;
        }

        var charts = LoadCharts(packDir, packName, info.Charts);
        if (charts.Count == 0)
        {
            Debug.LogWarning($"[CustomTrackLoader] 跳过谱包 {packName}：没有可用谱面");
            return null;
        }

        return new TrackData
        {
            MusicTitle = info.MusicTitle,
            MusicArtist = info.MusicArtist,
            MusicAlbum = info.MusicAlbum,
            MusicCoverArtist = info.MusicCoverArtist,
            MusicCover = LoadCover(packDir, info.MusicCoverPath),
            BPM = TryReadBpm(charts[0].ChartContent),
            ArchiveSignature = string.Empty,
            PreviewStartMs = info.PreviewStart,
            PreviewEndMs = info.PreviewEnd,
            Audio = new ExternalTrackAudio
            {
                FilePath = musicPath,
                StartOffsetMs = info.MusicStartOffset,
                DurationMs = durationMs,
            },
            Charts = charts.ToArray(),
        };
    }

    private static List<ChartData> LoadCharts(string packDir, string packName, ChartInfo[] sourceCharts)
    {
        var charts = new List<ChartData>();
        if (sourceCharts == null) return charts;

        var seenDifficulties = new HashSet<ChartDif>();
        foreach (var chart in sourceCharts)
        {
            if (chart == null) continue;

            int difficultyId = chart.DifficultyId;
            if (difficultyId < (int)ChartDif.EZ || difficultyId > (int)ChartDif.EX)
            {
                Debug.LogWarning($"[CustomTrackLoader] 谱包 {packName}：忽略非法难度编号 {difficultyId}");
                continue;
            }

            var difficulty = (ChartDif)difficultyId;
            if (!seenDifficulties.Add(difficulty))
            {
                Debug.LogWarning($"[CustomTrackLoader] 谱包 {packName}：忽略重复难度 {difficulty}");
                continue;
            }

            string chartPath = ResolvePackFile(packDir, chart.ChartPath);
            if (chartPath == null || !File.Exists(chartPath))
            {
                Debug.LogWarning($"[CustomTrackLoader] 谱包 {packName}：谱面文件不存在（{chart.ChartPath}）");
                continue;
            }

            charts.Add(new ChartData
            {
                Difficulty = difficulty,
                ChartArtist = chart.ChartArtist,
                ChartConstant = chart.ChartConstant,
                ChartContent = File.ReadAllText(chartPath),
            });
        }

        charts.Sort((a, b) => a.Difficulty.CompareTo(b.Difficulty));
        return charts;
    }

    private static Sprite LoadCover(string packDir, string coverPath)
    {
        string fullPath = ResolvePackFile(packDir, coverPath);
        if (fullPath == null || !File.Exists(fullPath)) return null;

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!texture.LoadImage(File.ReadAllBytes(fullPath)))
            {
                UnityEngine.Object.Destroy(texture);
                Debug.LogWarning($"[CustomTrackLoader] 封面解码失败：{coverPath}");
                return null;
            }

            texture.hideFlags = HideFlags.DontSave;
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
        catch (Exception e)
        {
            UnityEngine.Object.Destroy(texture);
            Debug.LogWarning($"[CustomTrackLoader] 封面加载失败：{coverPath}（{e.Message}）");
            return null;
        }
    }

    private static int TryReadBpm(string chartText)
    {
        try
        {
            var json = JObject.Parse(chartText.TrimStart((char)0xFEFF));
            double? bpm = json["Bpm"]?[0]?["Bpm"]?.Value<double>();
            return bpm.HasValue ? (int)Math.Round(bpm.Value) : 0;
        }
        catch
        {
            return 0;
        }
    }

    // 只接受包目录内的相对路径；拒绝绝对路径与 .. 越界
    private static string ResolvePackFile(string packDir, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) return null;

        string root = Path.GetFullPath(packDir);
        string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? root
            : root + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
    }

    // 只校验 PCM WAV 头并取 data 时长；不读取音频本体
    private static bool TryReadWavDuration(string path, out int durationMs)
    {
        durationMs = 0;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream, Encoding.ASCII, true);

            if (ReadChunkId(reader) != "RIFF") return false;
            reader.ReadInt32();
            if (ReadChunkId(reader) != "WAVE") return false;

            int audioFormat = 0;
            int byteRate = 0;
            int dataSize = 0;

            while (stream.Position + 8 <= stream.Length)
            {
                string chunkId = ReadChunkId(reader);
                int chunkSize = reader.ReadInt32();
                if (chunkSize < 0) return false;

                long nextChunk = stream.Position + chunkSize + (chunkSize & 1);

                if (chunkId == "fmt ")
                {
                    audioFormat = reader.ReadInt16();
                    reader.ReadInt16();   // channels
                    reader.ReadInt32();   // sampleRate
                    byteRate = reader.ReadInt32();
                    reader.ReadInt16();   // blockAlign
                    reader.ReadInt16();   // bitsPerSample
                }
                else if (chunkId == "data")
                {
                    dataSize = chunkSize;
                }

                if (audioFormat != 0 && dataSize > 0) break;
                stream.Position = nextChunk;
            }

            if (audioFormat != 1 || byteRate <= 0 || dataSize <= 0) return false;

            durationMs = (int)((long)dataSize * 1000L / byteRate);
            return durationMs > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string ReadChunkId(BinaryReader reader) => new string(reader.ReadChars(4));
}
