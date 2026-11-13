using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class OfficialTrackInfo
{
    [Header("乐曲名")] public string MusicTitle;
    [Header("曲师")] public string MusicArtist;
    [Header("专辑")] public string MusicAlbum;
    [Header("曲绘画师")] public string MusicCoverArtist;
    [Header("乐曲封面")] public Sprite MusicCover;
    [Header("BPM")] public int BPM;
    [Header("存档签名")] public string ArchiveSignature;
    [Header("预览起点（ms）")] public int PreviewStart;
    [Header("预览终点（ms）")] public int PreviewEnd;
    
    [Header("Wwise配置")] public WwiseSongPlayConfig WwiseConfig;
    
    [Header("谱面")] public OfficialChartInfo[] ChartInfos;
}
