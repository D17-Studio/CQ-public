using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单个官谱分类条目：分类 id + 该分类下的曲目。
/// </summary>
[Serializable]
public class OfficialCategoryEntry
{
    public OfficialCategory Category;
    public List<OfficialTrackAsset> Tracks = new();
}

/// <summary>
/// 官谱分类配置：集中声明每条官谱所属的分类，是官谱曲目与分类顺序的唯一来源。
/// </summary>
[CreateAssetMenu(fileName = "OfficialTrackCategoryConfig", menuName = "TrackService/OfficialTrackCategoryConfig")]
public class OfficialTrackCategoryConfig : ScriptableObject
{
    [SerializeField] private List<OfficialCategoryEntry> _entries = new();

    /// <summary>分类条目，列表顺序即分类顺序。</summary>
    public IReadOnlyList<OfficialCategoryEntry> Entries => _entries;
}
