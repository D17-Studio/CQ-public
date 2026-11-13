using UnityEngine;
using Zenject;
using VFXTest.Vfx;

/// <summary>
/// 把 VFX 运行时和服务绑定到 ProjectContext，作为跨场景单例。
/// </summary>
[CreateAssetMenu(fileName = "VfxInstaller", menuName = "Installers/VfxInstaller")]
public class VfxInstaller : ScriptableObjectInstaller
{
    public override void InstallBindings()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        // 挂在 ProjectContext 下，随容器跨场景常驻。
        Container.Bind<VfxRuntime>()
            .FromNewComponentOnNewGameObject()
            .WithGameObjectName("[VfxSystem]")
            .UnderTransform(ctx => ctx.Container.Resolve<Context>().transform)
            .AsSingle()
            .NonLazy();

        Container.Bind<VfxService>().AsSingle();
    }
}
