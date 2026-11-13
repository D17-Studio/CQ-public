using System.IO;
using UnityEngine;
using Zenject;

/// <summary>
/// 玩家配置安装器：绑定 PlayerConfigStore 跨场景单例到 DI 容器（playerConfig.json 落 persistentDataPath）。
/// </summary>
[CreateAssetMenu(fileName = "PlayerConfigInstaller", menuName = "Persistence/PlayerConfigInstaller")]
public class PlayerConfigInstaller : ScriptableObjectInstaller
{
    public override void InstallBindings()
    {
        var filePath = Path.Combine(Application.persistentDataPath, "playerConfig.json");
        Container.BindInstance(new PlayerConfigStore(filePath)).AsSingle();
    }
}
