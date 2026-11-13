using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 曲目集合：官谱与自制谱统一转换为运行时标准模型，提供统一查询入口。
/// 官谱的曲目来源与聚合顺序由 OfficialTrackCategoryConfig 决定；自制谱扫描外部目录。
/// </summary>
public class TrackCollection
{
    private readonly List<TrackData> _officialTracks = new();
    private readonly List<TrackData> _customTracks = new();
    private readonly List<TrackData> _allTracks = new();
    private readonly Dictionary<OfficialCategory, List<TrackData>> _officialTracksByCategory = new();

    public TrackCollection(OfficialTrackCategoryConfig officialConfig, string customTrackRoot)
    {
        BuildOfficialTracks(officialConfig);

        _customTracks.AddRange(CustomTrackLoader.LoadAll(customTrackRoot));

        _allTracks.AddRange(_officialTracks);
        _allTracks.AddRange(_customTracks);
    }

    /// <summary>获取全部曲目（官谱按 config 分类顺序拼接，自制谱在后）。</summary>
    public IReadOnlyList<TrackData> GetTracks() => _allTracks;

    /// <summary>获取全部官谱曲目（按 config 分类顺序）。</summary>
    public IReadOnlyList<TrackData> GetOfficialTracks() => _officialTracks;

    /// <summary>获取指定分类的官谱曲目；未配置或该分类无曲目时返回空列表。</summary>
    public IReadOnlyList<TrackData> GetOfficialTracks(OfficialCategory category) =>
        _officialTracksByCategory.TryGetValue(category, out var tracks)
            ? tracks
            : System.Array.Empty<TrackData>();

    /// <summary>获取全部自制谱曲目（目录名排序）。</summary>
    public IReadOnlyList<TrackData> GetCustomTracks() => _customTracks;

    private void BuildOfficialTracks(OfficialTrackCategoryConfig config)
    {
        if (config == null)
        {
            Debug.LogWarning("[TrackCollection] 未配置官谱分类（OfficialTrackCategoryConfig），官谱列表为空");
            return;
        }

        var seenTracks = new HashSet<OfficialTrackAsset>();

        foreach (var entry in config.Entries)
        {
            if (entry == null || entry.Category == OfficialCategory.None)
            {
                Debug.LogWarning("[TrackCollection] 官谱分类配置存在漏选条目（Category = None），已跳过");
                continue;
            }
            if (entry.Tracks == null) continue;

            if (_officialTracksByCategory.ContainsKey(entry.Category))
            {
                Debug.LogWarning($"[TrackCollection] 分类 {entry.Category} 存在重复条目，已忽略该条目");
                continue;
            }

            var categoryTracks = new List<TrackData>();
            _officialTracksByCategory.Add(entry.Category, categoryTracks);

            foreach (var asset in entry.Tracks)
            {
                if (asset == null || asset.TrackInfo == null)
                {
                    Debug.LogWarning($"[TrackCollection] 分类 {entry.Category} 中存在空的官谱资产，已跳过");
                    continue;
                }
                if (!seenTracks.Add(asset))
                {
                    Debug.LogWarning($"[TrackCollection] 官谱 {asset.name} 重复归属分类，已跳过（分类 {entry.Category}）");
                    continue;
                }

                var track = OfficialTrackAdapter.Convert(asset.TrackInfo);
                categoryTracks.Add(track);
                _officialTracks.Add(track);
            }
        }
    }
}
