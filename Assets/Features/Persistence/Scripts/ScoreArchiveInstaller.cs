using System.IO;
using UnityEngine;
using Zenject;

/// <summary>
/// 分数存档安装器：绑定 ScoreArchive 跨场景单例到 DI 容器（scores.json 落 persistentDataPath）。
/// </summary>
[CreateAssetMenu(fileName = "ScoreArchiveInstaller", menuName = "Persistence/ScoreArchiveInstaller")]
public class ScoreArchiveInstaller : ScriptableObjectInstaller
{
    public override void InstallBindings()
    {
        var filePath = Path.Combine(Application.persistentDataPath, "scores.json");
        Container.BindInstance(new ScoreArchive(filePath)).AsSingle();
    }
}
