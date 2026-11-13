using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 输入服务（纯 C#，InputInstaller 装配为 DI 跨场景单例）：设备 → 语义意图的翻译 + 键位重绑 + 覆盖持久化。
/// 消费端逐帧轮询（区域三读 / Back / 菜单方向）；重绑为一次性异步操作（回调式），
/// 捕获期间 IsRebinding 为真；捕获中与"刚取消那帧"的 ESC 由 BackDown() 自身抑制（消费者无需特判）。
/// 覆盖存 customBindings.json：构造时加载（坏档备份留证回默认），重绑 / 恢复默认时原子落盘，退出兜底重试。
/// </summary>
public class InputService : IDisposable
{
    private readonly string _filePath;
    private readonly InputActions _inputActions;
    private readonly Dictionary<JudgeKeyAction, InputAction> _keyActions;
    private readonly Dictionary<InputZone, InputAction[]> _inputZoneActions;

    private InputActionRebindingExtensions.RebindingOperation _rebindOperation;
    // 取消重绑的那一帧（frameCount）：该帧的 ESC 不再对外报 BackDown，防"取消重绑连带关弹层"
    private int _rebindCancelledFrame = -1;
    // 覆盖变更后置脏；保存成功清脏；退出兜底重试（同 PlayerConfigStore 的检查点语义）
    private bool _dirty;

    public InputService(string filePath, JudgeZoneCatalog zoneCatalog)
    {
        if (zoneCatalog == null)
            throw new ArgumentNullException(nameof(zoneCatalog));

        _filePath = filePath;
        _inputActions = new InputActions();

        _keyActions = new Dictionary<JudgeKeyAction, InputAction>(zoneCatalog.Actions.Count);
        foreach (JudgeKeyAction action in zoneCatalog.Actions)
        {
            _keyActions.Add(action, ResolveAction(action));
        }

        _inputZoneActions = new Dictionary<InputZone, InputAction[]>(ZoneInput.AllZones.Length);
        foreach (InputZone zone in ZoneInput.AllZones)
        {
            JudgeKeyAction[] keys = zoneCatalog.InputKeys(zone);
            var actions = new InputAction[keys.Length];

            for (int i = 0; i < keys.Length; i++)
                actions[i] = _keyActions[keys[i]];

            _inputZoneActions.Add(zone, actions);
        }

        LoadBindingsFromFile();
        _inputActions.Enable();
        Application.quitting += SaveBindingsToFile;
    }

    /// <summary>重绑捕获中（重绑 UI 的点击入口守卫用；ESC 的抑制已内联在 BackDown）。</summary>
    public bool IsRebinding => _rebindOperation != null;

    // ── 轮询口（消费端逐帧读；区域语义为"任一按键满足"） ──

    /// <summary>输入分区内任一按键本帧按下。</summary>
    public bool InputZoneDown(InputZone zone)
    {
        foreach (InputAction action in GetInputZone(zone))
        {
            if (action.WasPressedThisFrame())
                return true;
        }

        return false;
    }

    /// <summary>输入分区内任一按键按住中。</summary>
    public bool InputZoneHold(InputZone zone)
    {
        foreach (InputAction action in GetInputZone(zone))
        {
            if (action.IsPressed())
                return true;
        }

        return false;
    }

    /// <summary>输入分区内任一按键本帧松开。</summary>
    public bool InputZoneUp(InputZone zone)
    {
        foreach (InputAction action in GetInputZone(zone))
        {
            if (action.WasReleasedThisFrame())
                return true;
        }

        return false;
    }

    /// <summary>
    /// 返回键（ESC）本帧按下。捕获中与取消重绑的那一帧一律不报：
    /// 按 ESC 取消重绑的同一帧，取消回调先于消费者轮询执行——不抑制会连带触发关弹层。
    /// </summary>
    public bool BackDown()
    {
        if (IsRebinding || Time.frameCount == _rebindCancelledFrame) return false;
        return _inputActions.UIActions.Back.WasPressedThisFrame();
    }

    /// <summary>菜单方向键上：本帧按下。</summary>
    public bool MenuUp() => _inputActions.UIActions.MenuUp.WasPressedThisFrame();

    /// <summary>菜单方向键下：本帧按下。</summary>
    public bool MenuDown() => _inputActions.UIActions.MenuDown.WasPressedThisFrame();

    /// <summary>菜单方向键左：本帧按下。</summary>
    public bool MenuLeft() => _inputActions.UIActions.MenuLeft.WasPressedThisFrame();

    /// <summary>菜单方向键右：本帧按下。</summary>
    public bool MenuRight() => _inputActions.UIActions.MenuRight.WasPressedThisFrame();

    // ── 重绑（按键动作级；聚合判定区不可重绑，按配置中的单键动作重绑） ──

    /// <summary>某按键动作当前绑定的按键显示名（取第 0 条绑定，不含设备前缀）。</summary>
    public string GetKeyName(JudgeKeyAction action)
    {
        string effectivePath = GetKey(action).bindings[0].effectivePath;
        return InputControlPath.ToHumanReadableString(effectivePath,
                                                      InputControlPath.HumanReadableStringOptions.OmitDevice);
    }

    /// <summary>
    /// 启动按键重绑捕获：捕获期间该动作禁用；ESC 取消（原绑定保留、不落盘）；
    /// 换键成功即落盘并回调 onComplete。
    /// </summary>
    public void StartRebind(JudgeKeyAction action, Action onComplete = null, Action onCancel = null)
    {
        if (IsRebinding) return;   // 一次只允许一个捕获

        InputAction inputAction = GetKey(action);
        inputAction.Disable();

        _rebindOperation = inputAction.PerformInteractiveRebinding()
            .WithCancelingThrough("<Keyboard>/escape")
            .WithControlsHavingToMatchPath("<Keyboard>")
            .WithControlsExcluding("Mouse")
            .OnMatchWaitForAnother(0.1f)
            .OnComplete(op =>
            {
                op.Dispose();
                _rebindOperation = null;
                inputAction.Enable();

                Debug.Log($"[InputService] 重绑完成，新按键：{GetKeyName(action)}");
                _dirty = true;
                SaveBindingsToFile();
                onComplete?.Invoke();
            })
            .OnCancel(op =>
            {
                op.Dispose();
                _rebindOperation = null;
                inputAction.Enable();
                _rebindCancelledFrame = Time.frameCount;

                Debug.Log("[InputService] 重绑取消（原绑定保留）");
                onCancel?.Invoke();
            });
        _rebindOperation.Start();
    }

    /// <summary>恢复某按键动作的默认绑定（清除该动作的全部覆盖）并立即落盘。</summary>
    public void ResetBinding(JudgeKeyAction action)
    {
        GetKey(action).RemoveAllBindingOverrides();
        _dirty = true;
        SaveBindingsToFile();
    }

    /// <summary>停用全部输入动作（容器销毁时由 Zenject 调用）。</summary>
    public void Dispose()
    {
        _inputActions.Disable();
    }

    private InputAction[] GetInputZone(InputZone zone)
    {
        if (_inputZoneActions.TryGetValue(zone, out InputAction[] actions))
            return actions;

        throw new ArgumentOutOfRangeException(nameof(zone), zone, "未知输入分区");
    }

    private InputAction GetKey(JudgeKeyAction action)
    {
        if (_keyActions.TryGetValue(action, out InputAction inputAction))
            return inputAction;

        throw new ArgumentOutOfRangeException(nameof(action), action, "按键未在判定区配置中使用");
    }

    private InputAction ResolveAction(JudgeKeyAction action) => action switch
    {
        JudgeKeyAction.Left2 => _inputActions.GameActions.KeyL2,
        JudgeKeyAction.Left1 => _inputActions.GameActions.KeyL1,
        JudgeKeyAction.Right1 => _inputActions.GameActions.KeyR1,
        JudgeKeyAction.Right2 => _inputActions.GameActions.KeyR2,
        JudgeKeyAction.ExtraLeft0 => _inputActions.GameActions.KeyExtraLeft0,
        JudgeKeyAction.ExtraLeft1 => _inputActions.GameActions.KeyExtraLeft1,
        JudgeKeyAction.ExtraRight0 => _inputActions.GameActions.KeyExtraRight0,
        JudgeKeyAction.ExtraRight1 => _inputActions.GameActions.KeyExtraRight1,
        JudgeKeyAction.ExtraAll0 => _inputActions.GameActions.KeyExtraAll0,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "未实现的判定按键输入动作"),
    };

    // ── 覆盖持久化：坏档备份留证 / 原子写 tmp+replace / 失败保脏下次重试 ──

    private void LoadBindingsFromFile()
    {
        if (!File.Exists(_filePath)) return;   // 首次运行：无覆盖 = 默认绑定

        try
        {
            _inputActions.asset.LoadBindingOverridesFromJson(File.ReadAllText(_filePath));
            Debug.Log($"[InputService] 已加载键位覆盖：{_filePath}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[InputService] 键位覆盖损坏，已备份留证并回默认绑定：{_filePath}\n{e.Message}");
            BackupBrokenFile();
        }
    }

    private void SaveBindingsToFile()
    {
        if (!_dirty) return;

        try
        {
            string tmpPath = _filePath + ".tmp";
            File.WriteAllText(tmpPath, _inputActions.asset.SaveBindingOverridesAsJson());

            if (File.Exists(_filePath))
                File.Replace(tmpPath, _filePath, null);
            else
                File.Move(tmpPath, _filePath);

            _dirty = false;
        }
        catch (Exception e)
        {
            Debug.LogError($"[InputService] 键位覆盖写盘失败（内存绑定仍生效，下次保存重试）：{_filePath}\n{e.Message}");
        }
    }

    private void BackupBrokenFile()
    {
        try
        {
            File.Copy(_filePath, _filePath + ".bak", true);
            File.Delete(_filePath);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[InputService] 坏档备份失败（继续以默认绑定运行）：{e.Message}");
        }
    }
}
