using System;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace CQMusicGame.Shared
{
    /// <summary>
    /// 曲目信息的序列化 / 反序列化。
    /// 格式：与 ChartSerializer 一致的 JSON，字段名与 TrackInfo / ChartInfo 的字段一一对应；
    /// 缺失字段按默认值处理，多余字段忽略。旧版逐行 Key:Value 格式已不再支持。
    /// </summary>
    /// <remarks>
    /// 注意：本件中的 GUID 识文件位于 Unity 工程 Assets 目录下，Unity 依靠同名 .meta 文别资产。
    /// 重命名本文件时必须把 .cs 与 .meta 一起改名（或删除旧 .meta），否则 Unity 会视为删除旧资产再新增资产，
    /// 旧 GUID 的引用会失效，并留下孤立的 .meta。当前 Unity 端尚未使用本类，但以后会接入，届时不要随意改名。
    /// </remarks>
    public static class TrackInfoSerializer
    {
        private const int CurrentVersion = 1;

        private static readonly JsonSerializerSettings Settings = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            Formatting = Formatting.Indented,
            Culture = CultureInfo.InvariantCulture,
        };

        /// <summary>
        /// 解析曲目信息文本；空文本返回空 TrackInfo，格式或内容错误时抛出 ChartException
        /// </summary>
        public static TrackInfo Parse(string text)
        {
            if (text == null)
                return new TrackInfo();

            // 去掉 UTF-8 BOM（Windows 记事本保存的文件常带），再判断是否为空
            string json = text.TrimStart((char)0xFEFF);
            if (string.IsNullOrWhiteSpace(json))
                return new TrackInfo();

            TrackInfo info;
            try
            {
                info = JsonConvert.DeserializeObject<TrackInfo>(json, Settings);
            }
            catch (JsonException ex)
            {
                throw new ChartException($"曲目信息解析失败：{ex.Message}", ex);
            }

            if (info == null)
                throw new ChartException("曲目信息内容为空");

            // 版本校验：缺失 Version 视为版本 1（兼容手写文件），版本不匹配直接报错，
            // 避免用错版本的解析逻辑读到错误的数据
            if (info.Version.HasValue)
            {
                if (info.Version.Value != CurrentVersion)
                    throw new ChartException($"不支持的曲目信息版本：{info.Version.Value}（当前支持 {CurrentVersion}）");
            }

            Normalize(info);
            return info;
        }

        /// <summary>
        /// 序列化为 JSON 文本（Charts 过滤 null 并按难度编号升序）
        /// </summary>
        public static string Serialize(TrackInfo info)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            // 不修改调用方对象：构造浅拷贝统一写入 Version，并整理 Charts 的顺序
            var copy = new TrackInfo
            {
                Version = CurrentVersion,
                MusicTitle = info.MusicTitle,
                MusicCoverPath = info.MusicCoverPath,
                MusicPath = info.MusicPath,
                MusicStartOffset = info.MusicStartOffset,
                PreviewStart = info.PreviewStart,
                PreviewEnd = info.PreviewEnd,
                PreviewBpm = info.PreviewBpm,
                MusicArtist = info.MusicArtist,
                MusicAlbum = info.MusicAlbum,
                MusicCoverArtist = info.MusicCoverArtist,
                Charts = info.Charts?
                    .Where(c => c != null)
                    .OrderBy(c => c.DifficultyId)
                    .ToArray(),
            };

            return JsonConvert.SerializeObject(copy, Settings);
        }

        /// <summary>
        /// 补齐缺失字段：字符串归一为空串、过滤 null 谱面条目，保证与旧解析器的产出一致
        /// </summary>
        private static void Normalize(TrackInfo info)
        {
            info.MusicTitle ??= string.Empty;
            info.MusicCoverPath ??= string.Empty;
            info.MusicPath ??= string.Empty;
            info.MusicArtist ??= string.Empty;
            info.MusicAlbum ??= string.Empty;
            info.MusicCoverArtist ??= string.Empty;

            if (info.Charts == null)
                return;

            var charts = info.Charts.Where(c => c != null).ToArray();
            if (charts.Length == 0)
            {
                info.Charts = null;
                return;
            }

            info.Charts = charts;
            foreach (var chart in info.Charts)
            {
                chart.ChartPath ??= string.Empty;
                chart.ChartArtist ??= string.Empty;
            }
        }
    }
}
