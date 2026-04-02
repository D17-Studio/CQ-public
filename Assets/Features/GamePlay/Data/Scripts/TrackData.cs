using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "TrackData" , menuName = "Data/TrackData")]
public class TrackData : ScriptableObject
{
    [Header("乐曲名")] public string MusicTitle;
    [Header("曲师")] public string MusicArtist;
    [Header("专辑")] public string MusicAlbum;
    [Header("乐曲封面")] public Sprite MusicCover;
    [Header("乐曲")] public AudioClip MusicClip;
    [Header("截取片段")] public Vector2 MusicExcerpt;
    [Header("BPM")] public int BPM;
    
    [Header("谱面")] public ChartData[] ChartInfos;
}
