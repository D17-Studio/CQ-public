using System.Collections.Generic;
using UnityEngine;
using Zenject;

/// <summary>
/// 官谱资产安装器，注入所有 OfficialTrackAsset 到 DI 容器
/// </summary>
[CreateAssetMenu(fileName = "TrackAssetInstaller", menuName = "TrackService/TrackAssetInstaller")]
public class TrackAssetInstaller : ScriptableObjectInstaller
{
    [SerializeField] private List<OfficialTrackAsset> _tracks;

    public override void InstallBindings()
    {
        var trackCollection = new TrackCollection(_tracks);
        Container.BindInstance(trackCollection).AsSingle();

        var trackSelectionService = new TrackSelectionService();
        
        //使用0号歌曲
        trackSelectionService.SelectedTrack = trackCollection.GetTracks()[0];
        trackSelectionService.SelectedChart = trackSelectionService.SelectedTrack.ChartInfos[0];
        
        Container.BindInstance(trackSelectionService).AsSingle();
    }
}
