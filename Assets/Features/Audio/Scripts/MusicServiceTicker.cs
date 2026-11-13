using UnityEngine;

/// <summary>
/// 常驻更新器：驱动 MusicService 的延迟开播与预览循环。
/// </summary>
public class MusicServiceTicker : MonoBehaviour
{
    [System.NonSerialized] public MusicService Service;

    private void Update() => Service?.Tick();
}
