using System;
using System.IO;
using UnityEngine;
using Zenject;

/// <summary>
/// 输入服务安装器（SO）：绑定 InputService 跨场景单例到 DI 容器（customBindings.json 落 persistentDataPath）。
/// </summary>
[CreateAssetMenu(fileName = "InputInstaller", menuName = "Input/InputInstaller")]
public class InputInstaller : ScriptableObjectInstaller
{
    [SerializeField] private JudgeZoneConfig _zoneConfig;

    public override void InstallBindings()
    {
        if (_zoneConfig == null)
            throw new InvalidOperationException("InputInstaller 未配置 JudgeZoneConfig");

        var zoneCatalog = new JudgeZoneCatalog(_zoneConfig.Zones);
        Container.BindInstance(zoneCatalog).AsSingle();

        var filePath = Path.Combine(Application.persistentDataPath, "customBindings.json");
        Container.BindInstance(new InputService(filePath, zoneCatalog)).AsSingle();
    }
}
