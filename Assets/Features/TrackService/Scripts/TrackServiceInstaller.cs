using System.IO;
using UnityEngine;
using Zenject;

/// <summary>
/// 曲目服务安装器：注入官谱分类配置构建的 TrackCollection 到 DI 容器
/// </summary>
[CreateAssetMenu(fileName = "TrackServiceInstaller", menuName = "TrackService/TrackServiceInstaller")]
public class TrackServiceInstaller : ScriptableObjectInstaller
{
    [SerializeField] private OfficialTrackCategoryConfig _categoryConfig;

    public override void InstallBindings()
    {
        string customTrackRoot = Path.Combine(Path.GetDirectoryName(Application.dataPath), "CustomTracks");
        Debug.Log($"[TrackService] 自制谱扫描目录：{customTrackRoot}");

        var trackCollection = new TrackCollection(_categoryConfig, customTrackRoot);
        Container.BindInstance(trackCollection).AsSingle();

        // 选曲不再预置默认值：由菜单（SongSelectPresenter）在点开始时写入。
        var playSessionService = new PlaySessionService();
        Container.BindInstance(playSessionService).AsSingle();
    }
}
