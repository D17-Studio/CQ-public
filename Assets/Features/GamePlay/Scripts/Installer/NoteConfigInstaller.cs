using UnityEngine;
using Zenject;

/// <summary>
/// Note 配置安装器（SO）：聚合判定与视觉配置，分别绑定到 DI 容器。
/// </summary>
[CreateAssetMenu(fileName = "NoteConfigInstaller", menuName = "Installers/NoteConfigInstaller")]
public class NoteConfigInstaller : ScriptableObjectInstaller<NoteConfigInstaller>
{
    [SerializeField] private NoteJudgeConfig _judge = new();
    [SerializeField] private NoteVisualConfig _visual = new();

    public override void InstallBindings()
    {
        Container.BindInstance(_judge);
        Container.BindInstance(_visual);
    }
}
