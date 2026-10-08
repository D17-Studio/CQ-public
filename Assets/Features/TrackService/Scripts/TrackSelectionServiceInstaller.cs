using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

public class TrackSelectionServiceInstaller : MonoInstaller
{
    public override void InstallBindings()
    {
        Container.Bind<TrackSelectionService>().AsSingle();
    }
}
