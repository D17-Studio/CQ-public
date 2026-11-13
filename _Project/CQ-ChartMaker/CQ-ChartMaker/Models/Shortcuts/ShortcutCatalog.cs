using System;
using System.Collections.Generic;
using Avalonia.Input;

namespace CQ_ChartMaker.Models;

/// <summary> 可被快捷键触发的动作 </summary>
public enum ShortcutAction
{
    ToggleHitSoundMute,
    PlaceDot,
    PlaceDash,
    PlaceMute,
    PlaceTune,
    PlaceMotionBpm,
    ToggleGhostFlip,
    TogglePlayPause,
    Copy,
    Cut,
    SelectAll,
    Undo,
    Redo,
    Save,
    Delete,
    UiZoomIn,
    UiZoomOut
}

/// <summary> 单个快捷键的定义：默认键位、是否可改、是否在设置页显示 </summary>
/// <param name="Action">动作枚举</param>
/// <param name="Id">持久化用的稳定标识（不随显示名变化）</param>
/// <param name="DisplayName">设置页显示名称</param>
/// <param name="DefaultGesture">默认键位</param>
/// <param name="IsEditable">是否允许用户改键</param>
/// <param name="ShowInSettings">是否在设置页显示</param>
public sealed record ShortcutDefinition(
    ShortcutAction Action,
    string Id,
    string DisplayName,
    KeyGesture DefaultGesture,
    bool IsEditable,
    bool ShowInSettings = true);

/// <summary>
/// 快捷键目录：集中维护全部动作与默认键位。
/// 顺序同时决定运行时匹配优先级（固定键在前），以及设置页的显示顺序。
/// </summary>
public static class ShortcutCatalog
{
    /// <summary> 全部快捷键定义 </summary>
    public static IReadOnlyList<ShortcutDefinition> All { get; } = new ShortcutDefinition[]
    {
        // ===== 固定键（不可修改，在设置页作为提示显示） =====
        new(ShortcutAction.Copy, "Copy", "复制", new KeyGesture(Key.C, KeyModifiers.Control), false),
        new(ShortcutAction.Cut, "Cut", "剪贴", new KeyGesture(Key.X, KeyModifiers.Control), false),
        new(ShortcutAction.SelectAll, "SelectAll", "全选", new KeyGesture(Key.A, KeyModifiers.Control), false),
        new(ShortcutAction.Undo, "Undo", "撤回", new KeyGesture(Key.Z, KeyModifiers.Control), false),
        new(ShortcutAction.Redo, "Redo", "重做", new KeyGesture(Key.Z, KeyModifiers.Control | KeyModifiers.Shift), false),
        new(ShortcutAction.Save, "Save", "保存", new KeyGesture(Key.S, KeyModifiers.Control), false),

        // ===== 固定键（不显示在设置页，仅保留原行为） =====
        new(ShortcutAction.Delete, "Delete", "删除", new KeyGesture(Key.Delete), false, false),
        new(ShortcutAction.UiZoomIn, "UiZoomIn", "UI 放大", new KeyGesture(Key.OemPlus), false, false),
        new(ShortcutAction.UiZoomOut, "UiZoomOut", "UI 缩小", new KeyGesture(Key.OemMinus), false, false),

        // ===== 可改键（设置页显示并允许重设） =====
        new(ShortcutAction.ToggleHitSoundMute, "ToggleHitSoundMute", "静音打击音效", new KeyGesture(Key.S), true),
        new(ShortcutAction.PlaceDot, "PlaceDot", "放置 dot", new KeyGesture(Key.Q), true),
        new(ShortcutAction.PlaceDash, "PlaceDash", "放置 dash", new KeyGesture(Key.W), true),
        new(ShortcutAction.PlaceMute, "PlaceMute", "放置 mute", new KeyGesture(Key.E), true),
        new(ShortcutAction.PlaceTune, "PlaceTune", "放置 tune", new KeyGesture(Key.R), true),
        new(ShortcutAction.PlaceMotionBpm, "PlaceMotionBpm", "放置 event/BPM", new KeyGesture(Key.T), true),
        new(ShortcutAction.ToggleGhostFlip, "ToggleGhostFlip", "复制/剪贴时翻转", new KeyGesture(Key.F), true),
        new(ShortcutAction.TogglePlayPause, "TogglePlayPause", "播放/暂停", new KeyGesture(Key.Space), true),
    };

    /// <summary>
    /// 读取某个动作当前生效的键位：优先用户配置，缺失或格式损坏时回退默认值。
    /// </summary>
    public static KeyGesture GetGesture(ShortcutDefinition definition, IReadOnlyDictionary<string, string> bindings)
    {
        if (bindings.TryGetValue(definition.Id, out string? text) && !string.IsNullOrWhiteSpace(text))
        {
            try
            {
                return KeyGesture.Parse(text);
            }
            catch
            {
                // 手改 settings.json 导致键位字符串非法：静默回退默认键位
            }
        }

        return definition.DefaultGesture;
    }

    /// <summary> 把键位格式化为设置页显示文本 </summary>
    public static string FormatGesture(KeyGesture gesture) => gesture.ToString();

    /// <summary> 是否为纯修饰键（改键捕获时忽略，等待主键） </summary>
    public static bool IsModifierOnly(Key key) => key is
        Key.None or
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or
        Key.LWin or Key.RWin;
}
