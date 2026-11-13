using System;
using System.Collections.Generic;
using CQMusicGame.Shared;

/// <summary>
/// 判定区运行时视图：由 JudgeZoneConfig 派生 JudgeZone → JudgeKeyAction 集合。
/// </summary>
public sealed class JudgeZoneCatalog
{
    private static readonly JudgeZone[] JudgeZones =
    {
        JudgeZone.All, JudgeZone.Left, JudgeZone.Right,
        JudgeZone.L1, JudgeZone.L2, JudgeZone.R1, JudgeZone.R2
    };

    private readonly Dictionary<JudgeZone, JudgeKeyAction[]> _zoneKeys;
    private readonly Dictionary<InputZone, JudgeKeyAction[]> _inputZoneKeys;
    private readonly int[] _inputZoneJudgeMasks;

    public JudgeZoneCatalog(JudgeZoneConfigData zones)
    {
        ValidateAction(zones.L1);
        ValidateAction(zones.L2);
        ValidateAction(zones.R1);
        ValidateAction(zones.R2);
        ValidateActions(zones.ExtraLeft);
        ValidateActions(zones.ExtraRight);
        ValidateActions(zones.ExtraAll);

        JudgeKeyAction[] left = Merge(new[] { zones.L1, zones.L2 }, zones.ExtraLeft);
        JudgeKeyAction[] right = Merge(new[] { zones.R1, zones.R2 }, zones.ExtraRight);

        _zoneKeys = new Dictionary<JudgeZone, JudgeKeyAction[]>
        {
            { JudgeZone.L1, new[] { zones.L1 } },
            { JudgeZone.L2, new[] { zones.L2 } },
            { JudgeZone.R1, new[] { zones.R1 } },
            { JudgeZone.R2, new[] { zones.R2 } },
            { JudgeZone.Left, left },
            { JudgeZone.Right, right },
            { JudgeZone.All, Merge(left, right, zones.ExtraAll) }
        };

        Actions = Merge(new[] { zones.L1, zones.L2, zones.R1, zones.R2 },
                        zones.ExtraLeft, zones.ExtraRight, zones.ExtraAll);

        // 输入分区互斥：同一按键归入首个命中的分区，保证一次点击只产生一条 ZoneInput
        var claimed = new HashSet<JudgeKeyAction>();
        _inputZoneKeys = new Dictionary<InputZone, JudgeKeyAction[]>();
        AddInputZone(InputZone.L1, new[] { zones.L1 }, claimed);
        AddInputZone(InputZone.L2, new[] { zones.L2 }, claimed);
        AddInputZone(InputZone.R1, new[] { zones.R1 }, claimed);
        AddInputZone(InputZone.R2, new[] { zones.R2 }, claimed);
        AddInputZone(InputZone.ExtraLeft, zones.ExtraLeft, claimed);
        AddInputZone(InputZone.ExtraRight, zones.ExtraRight, claimed);
        AddInputZone(InputZone.ExtraAll, zones.ExtraAll, claimed);

        _inputZoneJudgeMasks = BuildInputZoneJudgeMasks();
    }

    /// <summary> 配置中使用的全部按键动作（去重，固定顺序） </summary>
    public IReadOnlyList<JudgeKeyAction> Actions { get; }

    /// <summary> 查询判定区包含的按键动作（缓存数组，调用方勿修改） </summary>
    public JudgeKeyAction[] Keys(JudgeZone zone) => _zoneKeys[zone];

    /// <summary> 查询输入分区独占的按键动作（同一按键只归属首个分区，调用方勿修改） </summary>
    public JudgeKeyAction[] InputKeys(InputZone zone) => _inputZoneKeys[zone];

    /// <summary> 判断输入分区能否命中指定判定区（按按键动作集合是否相交） </summary>
    public bool Matches(JudgeZone zone, InputZone inputZone)
        => (_inputZoneJudgeMasks[(int)inputZone] & (1 << (int)zone)) != 0;

    private int[] BuildInputZoneJudgeMasks()
    {
        var masks = new int[ZoneInput.AllZones.Length];

        foreach (InputZone inputZone in ZoneInput.AllZones)
        {
            JudgeKeyAction[] inputKeys = InputKeys(inputZone);
            int mask = 0;

            foreach (JudgeZone zone in JudgeZones)
            {
                if (Intersects(inputKeys, Keys(zone)))
                    mask |= 1 << (int)zone;
            }

            masks[(int)inputZone] = mask;
        }

        return masks;
    }

    private static bool Intersects(JudgeKeyAction[] first, JudgeKeyAction[] second)
    {
        foreach (JudgeKeyAction a in first)
        {
            foreach (JudgeKeyAction b in second)
            {
                if (a == b)
                    return true;
            }
        }

        return false;
    }

    private void AddInputZone(InputZone zone, JudgeKeyAction[] actions, HashSet<JudgeKeyAction> claimed)
    {
        var keys = new List<JudgeKeyAction>(actions?.Length ?? 0);

        if (actions != null)
        {
            foreach (JudgeKeyAction action in actions)
            {
                if (claimed.Add(action))
                    keys.Add(action);
            }
        }

        _inputZoneKeys.Add(zone, keys.ToArray());
    }

    private static void ValidateAction(JudgeKeyAction action)
    {
        if (!Enum.IsDefined(typeof(JudgeKeyAction), action))
            throw new ArgumentException($"判定区配置包含未定义的按键动作：{action}");
    }

    private static void ValidateActions(JudgeKeyAction[] actions)
    {
        if (actions == null)
            return;

        foreach (JudgeKeyAction action in actions)
            ValidateAction(action);
    }

    private static JudgeKeyAction[] Merge(params JudgeKeyAction[][] groups)
    {
        var keys = new List<JudgeKeyAction>();

        foreach (JudgeKeyAction[] group in groups)
        {
            if (group == null)
                continue;

            foreach (JudgeKeyAction action in group)
            {
                if (!keys.Contains(action))
                    keys.Add(action);
            }
        }

        return keys.ToArray();
    }
}
