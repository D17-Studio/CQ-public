using UnityEngine;

/// <summary>
/// 官谱 SO 包装，直接挂在 ScriptableObject 资产上
/// </summary>
[CreateAssetMenu(fileName = "OfficialTrack", menuName = "TrackService/OfficialTrack")]
public class OfficialTrackAsset : ScriptableObject
{
    public OfficialTrackInfo TrackInfo;
}
