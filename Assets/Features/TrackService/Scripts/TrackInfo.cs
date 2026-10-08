using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class TrackInfo
{
    [Header("乐曲名")] public string MusicTitle;
    [Header("曲师")] public string MusicArtist;
    [Header("专辑")] public string MusicAlbum;
    [Header("乐曲封面")] public Sprite MusicCover;
    [Header("乐曲路径")] public string MusicPath;
    [Header("截取片段")] public Vector2 MusicExcerpt;
    [Header("BPM")] public int BPM;
    
    [Header("Wwise配置")] public WwiseSongPlayConfig WwiseConfig;
    
    [Header("谱面")] public ChartInfo[] ChartInfos;
}
