using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;

namespace CQ_ChartMaker.Models;

/// <summary> 运行时快捷键管理器：读取用户键位配置，负责按键匹配与写回 </summary>
public sealed class ShortcutManagerModel
{
    private readonly UserSettingsModel _settings;

    public ShortcutManagerModel(UserSettingsModel settings)
    {
        _settings = settings;
    }

    /// <summary> 获取动作当前生效的键位（用户配置优先，缺失/损坏时用默认值） </summary>
    public KeyGesture GetGesture(ShortcutAction action)
    {
        ShortcutDefinition definition = ShortcutCatalog.All.First(d => d.Action == action);
        return ShortcutCatalog.GetGesture(definition, _settings.KeyBindings);
    }

    /// <summary>
    /// 把按键解析为动作：按目录顺序匹配，固定键优先于可改键。
    /// Key 与 KeyModifiers 必须完全相等，避免 Ctrl+Shift+Z 误触 Ctrl+Z。
    /// </summary>
    public bool TryResolve(Key key, KeyModifiers modifiers, out ShortcutAction action)
    {
        foreach (ShortcutDefinition definition in ShortcutCatalog.All)
        {
            KeyGesture gesture = ShortcutCatalog.GetGesture(definition, _settings.KeyBindings);
            if (gesture.Key == key && gesture.KeyModifiers == modifiers)
            {
                action = definition.Action;
                return true;
            }
        }

        action = default;
        return false;
    }

    /// <summary> 写入改键结果：整体替换字典，触发 UserSettingsModel 的属性变更与自动保存 </summary>
    public void SetGesture(ShortcutAction action, KeyGesture gesture)
    {
        ShortcutDefinition definition = ShortcutCatalog.All.First(d => d.Action == action);
        var bindings = new Dictionary<string, string>(_settings.KeyBindings)
        {
            [definition.Id] = gesture.ToString()
        };
        _settings.KeyBindings = bindings;
    }
}
