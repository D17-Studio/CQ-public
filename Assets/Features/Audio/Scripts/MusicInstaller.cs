using UnityEngine;
using Zenject;

/// <summary>
/// 音乐服务安装器（SO）：绑定 MusicService 跨场景单例到 DI 容器。
/// </summary>
[CreateAssetMenu(fileName = "MusicInstaller", menuName = "Audio/MusicInstaller")]
public class MusicInstaller : ScriptableObjectInstaller
{
    [SerializeField] private AK.Wwise.Bank _musicBank;
    [SerializeField] private AK.Wwise.Event _externalMusicEvent;

    public override void InstallBindings()
    {
        Container.BindInstance(new MusicService(_musicBank, _externalMusicEvent)).AsSingle();
    }
}
