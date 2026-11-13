using Zenject;

/// <summary>
/// 暂停输入路由（纯 C#，ITickable 轮询 ESC）：Playing → 暂停；Paused → 发起恢复倒数；
/// 其余阶段（Loading / Resuming / Result）忽略。取代临时测试键 PauseTestKey。
/// </summary>
public class PauseInputRouter : ITickable
{
    private readonly TrackController _trackController;
    private readonly InputService _inputService;

    public PauseInputRouter(TrackController trackController, InputService inputService)
    {
        _trackController = trackController;
        _inputService = inputService;
    }

    public void Tick()
    {
        if (!_inputService.BackDown()) return;

        switch (_trackController.Phase)
        {
            case TrackPhase.Playing:
                _trackController.Pause();
                break;
            case TrackPhase.Paused:
                _trackController.RequestResume();
                break;
        }
    }
}
