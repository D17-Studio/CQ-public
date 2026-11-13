using UnityEngine;
using Zenject;

/// <summary>
/// MenuScene 的 Zenject 装配入口：绑定 Presenter（WithArguments(view)）与 Navigator。
/// </summary>
public class MenuSceneInstaller : MonoInstaller
{
    // 选歌页 View。
    [SerializeField] private SongSelectView _songSelectView;
    // 设置弹层 View。
    [SerializeField] private SettingsPanelView _settingsPanelView;

    public override void InstallBindings()
    {
        // 导航器（ITickable 每帧轮询 BackDown）
        Container.BindInterfacesAndSelfTo<MenuNavigator>().AsSingle();

        // 其余依赖由 ProjectContext 继承解析；view 是场景手拖引用，经 WithArguments 传入
        Container.BindInterfacesAndSelfTo<SongSelectPresenter>()
                 .AsSingle()
                 .WithArguments(_songSelectView);

        // 设置弹层（常驻场景、默认隐藏）
        Container.BindInterfacesAndSelfTo<SettingsPresenter>()
                 .AsSingle()
                 .WithArguments(_settingsPanelView);
    }
}
