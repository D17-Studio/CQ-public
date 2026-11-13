using UnityEngine;
using Zenject;

/// <summary>
/// 设置弹层大脑（纯 C#，由 MenuSceneInstaller 装配；面板常驻场景、默认隐藏）。
/// 持开合状态；打开 / 关闭均经导航器（ESC 与关闭按钮同一出口）。
/// 流速 / 偏移：单击步进、边界钳制，即时写 store 内存，关闭时落盘。
/// 键位重绑暂缓：相关代码已注释，等核心重构完成后按 Zone / 按键动作新 API 恢复。
/// </summary>
public class SettingsPresenter : IInitializable
{
    // 流速（ScrollSpeed）与偏移（InputOffsetMs）的步进 / 边界
    private const int SpeedMin = 500;
    private const int SpeedMax = 2000;
    private const int SpeedStep = 100;
    private const int OffsetMinMs = -1000;
    private const int OffsetMaxMs = 1000;
    private const int OffsetStepMs = 10;

    // 键位重绑暂缓（核心重构完成后恢复）
    // private const int KeybindRowCount = 7;

    private readonly MenuNavigator _navigator;
    private readonly PlayerConfigStore _store;
    // 键位重绑暂缓（核心重构完成后恢复）
    // private readonly InputService _inputService;
    private readonly SettingsPanelView _view;

    /// <summary>弹层是否打开。</summary>
    public bool IsOpen { get; private set; }

    public SettingsPresenter(MenuNavigator navigator, PlayerConfigStore store, SettingsPanelView view)
    {
        _navigator = navigator;
        _store = store;
        _view = view;
    }

    public void Initialize()
    {
        _navigator.AttachSettings(this);
        _view.CloseClicked += OnCloseClicked;
        _view.SpeedStepRequested += OnSpeedStepRequested;
        _view.OffsetStepRequested += OnOffsetStepRequested;
        // 键位重绑暂缓（核心重构完成后恢复）
        // _view.KeybindRowClicked += OnKeybindRowClicked;
        // _view.ResetClicked += OnResetClicked;
        _view.Hide();   // 初始隐藏
    }

    /// <summary>打开：重读 store 数值下推。</summary>
    public void Open()
    {
        IsOpen = true;
        _view.SetSpeed(Mathf.RoundToInt(_store.ScrollSpeed));
        _view.SetOffset(_store.InputOffsetMs);
        // 键位重绑暂缓（核心重构完成后恢复）
        // RefreshKeybindNames();
        _view.Show();
    }

    /// <summary>关闭：隐藏面板并检查点落盘（没改过不写盘）。</summary>
    public void Close()
    {
        IsOpen = false;
        _view.Hide();
        _store.Save();
    }

    private void OnCloseClicked() => _navigator.CloseTop();

    // 流速：± 一档一步、边界钳制；即时写 store，落盘在 Close
    private void OnSpeedStepRequested(int delta)
    {
        int current = Mathf.RoundToInt(_store.ScrollSpeed);
        int next = Mathf.Clamp(current + delta * SpeedStep, SpeedMin, SpeedMax);
        if (next == current) return;

        _store.ScrollSpeed = next;
        _view.SetSpeed(next);
    }

    // 偏移：± 一档一步、边界钳制；同上
    private void OnOffsetStepRequested(int delta)
    {
        int current = _store.InputOffsetMs;
        int next = Mathf.Clamp(current + delta * OffsetStepMs, OffsetMinMs, OffsetMaxMs);
        if (next == current) return;

        _store.InputOffsetMs = next;
        _view.SetOffset(next);
    }

    // ── 键位重绑暂缓（核心重构完成后恢复） ──

    /*
    // 键位区 7 行下推
    private void RefreshKeybindNames()
    {
        for (int rowIndex = 0; rowIndex < KeybindRowCount; rowIndex++)
        {
            _view.SetKeyName(rowIndex, _inputService.GetLaneKeyName(rowIndex - 3));
        }
    }

    // 点键位行：显示提示层 → 启动捕获；完成 / 取消：收提示 + 刷新该行
    private void OnKeybindRowClicked(int rowIndex)
    {
        if (_inputService.IsRebinding) return;

        _view.SetPromptActive(true);
        _inputService.StartRebind(rowIndex - 3,
                                  () => FinishRebind(rowIndex),
                                  () => FinishRebind(rowIndex));
    }

    // 恢复默认：清全部覆盖后刷新 7 行
    private void OnResetClicked()
    {
        if (_inputService.IsRebinding) return;

        for (int lane = -3; lane <= 3; lane++)
        {
            _inputService.ResetBinding(lane);
        }
        RefreshKeybindNames();
    }

    private void FinishRebind(int rowIndex)
    {
        _view.SetPromptActive(false);
        _view.SetKeyName(rowIndex, _inputService.GetLaneKeyName(rowIndex - 3));
    }
    */
}
