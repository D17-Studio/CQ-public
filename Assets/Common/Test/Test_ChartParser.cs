using System.Collections.Generic;
using CQMusicGame.Shared;
using UnityEngine;

/// <summary>
/// ChartParser 测试：Note / Tune / LaneMotion / BPM 的解析与序列化
/// </summary>
public class Test_ChartParser : MonoBehaviour
{
    private const string SampleChart =
        @"Dot - Time:10000 ; Lane:0 ; Duration:0 ; Appear:-5000 ; Speed:Default()
Dash - Time:12000 ; Lane:1 ; Duration:2000 ; Appear:-3000 ; Speed:Multiplier(1.5)
 - Tune - Time:13000 ; Direction:Left ; Motion:Relative(-1,(-200,0)) ; Duration:100
 - Tune - Time:14000 ; Direction:Right ; Motion:Absolute((300,50))
Mute - Time:15000 ; Lane:-2 ; Duration:500 ; Appear:-3000 ; Speed:Fixed(600)
Dot - Time:16000 ; Lane:3 ; Duration:0 ; Appear:-3000 ; Speed:Default()
Dash - Time:18000 ; Lane:0 ; Duration:1000 ; Appear:-3000 ; Speed:Default()
BPM - Time:0 ; BPM:170
BPM - Time:30000 ; BPM:180
LaneMotion - Time:10000 ; Lane:0 ; Duration:2000 ; Anchor:(0,0) ; Position:[Relative,(100,0),Linear]
LaneMotion - Time:12000 ; Lane:1 ; Duration:1500 ; Anchor:(50,50) ; Rotation:[Absolute,90,InQuad]
LaneMotion - Time:14000 ; Lane:-2 ; Duration:1000 ; Anchor:(0,0) ; Opacity:[Affect,(Head,Line,Key,Note),0.5,OutCubic]
";

    // ==================== Note 测试 ====================

    [ContextMenu("Test ParseNotes")]
    private void TestParseNotes()
    {
        Debug.Log("===== ParseNotes 测试 =====\n");

        try
        {
            List<NoteData> notes = ChartParser.ParseNotes(SampleChart);
            Debug.Log($"解析成功，共 {notes.Count} 条 NoteData：\n");

            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                string speedStr = n.SpeedMode switch
                {
                    NoteSpeedMode.Default => "Default()",
                    NoteSpeedMode.Multiplier => $"Multiplier({n.SpeedValue})",
                    NoteSpeedMode.Fixed => $"Fixed({n.SpeedValue})",
                    _ => "?"
                };
                string info = $"[{i}] {n.Type} | Time:{n.TargetTime}ms | Lane:{n.LaneIndex} | Dur:{n.SustainDuration}ms | Appear:{n.AppearOffset}ms | Speed:{speedStr}";

                if (n.Type == NoteType.Dash && n.LinkedTunes != null && n.LinkedTunes.Length > 0)
                {
                    info += $"\n  附属Tune({n.LinkedTunes.Length}个)：";
                    foreach (var t in n.LinkedTunes)
                    {
                        string motion = t.UseRelative
                            ? $"Relative(Lane:{t.TargetLane}, Offset:({t.PosValue.x},{t.PosValue.y}))"
                            : $"Absolute(({t.PosValue.x},{t.PosValue.y}))";
                        info += $"\n    Time:{t.TargetTime}ms | Dir:{t.Direction} | Dur:{t.Duration}ms | {motion}";
                    }
                }

                Debug.Log(info);
            }
        }
        catch (ChartException ex)
        {
            Debug.LogError($"解析失败：{ex.Message}");
        }
    }

    [ContextMenu("Test Note RoundTrip")]
    private void TestNoteRoundTrip()
    {
        Debug.Log("===== Note 双向转换测试 =====\n");

        try
        {
            var parsed1 = ChartParser.ParseNotes(SampleChart);
            string serialized = ChartParser.SerializeNotes(parsed1);
            var parsed2 = ChartParser.ParseNotes(serialized);

            Debug.Log("序列化输出：\n" + serialized);

            bool countsMatch = parsed1.Count == parsed2.Count;
            Debug.Log($"\n行数一致：{countsMatch} ({parsed1.Count} vs {parsed2.Count})");

            bool allMatch = true;
            for (int i = 0; i < parsed1.Count && i < parsed2.Count; i++)
            {
                var a = parsed1[i];
                var b = parsed2[i];

                if (!NotesEqual(a, b))
                {
                    Debug.LogWarning($"第{i}条不匹配！\n  P1: {NoteToString(a)}\n  P2: {NoteToString(b)}");
                    allMatch = false;
                }

                if (a.Type == NoteType.Dash && b.Type == NoteType.Dash)
                {
                    var ta = a.LinkedTunes ?? System.Array.Empty<TuneData>();
                    var tb = b.LinkedTunes ?? System.Array.Empty<TuneData>();
                    if (ta.Length != tb.Length)
                    {
                        Debug.LogWarning($"第{i}条 Tune数量不一致：{ta.Length} vs {tb.Length}");
                        allMatch = false;
                    }
                    else
                    {
                        for (int j = 0; j < ta.Length; j++)
                        {
                            if (!TunesEqual(ta[j], tb[j]))
                            {
                                Debug.LogWarning($"第{i}条 Tune[{j}]不匹配");
                                allMatch = false;
                            }
                        }
                    }
                }
            }

            Debug.Log(allMatch && countsMatch ? "Note 双向转换一致！" : "存在不匹配！");
        }
        catch (ChartException ex)
        {
            Debug.LogError($"测试失败：{ex.Message}");
        }
    }

    private static bool NotesEqual(NoteData a, NoteData b)
    {
        return a.Type == b.Type
            && a.TargetTime == b.TargetTime
            && a.LaneIndex == b.LaneIndex
            && a.SustainDuration == b.SustainDuration
            && a.AppearOffset == b.AppearOffset
            && a.SpeedMode == b.SpeedMode
            && (a.SpeedMode == NoteSpeedMode.Default || Mathf.Approximately(a.SpeedValue, b.SpeedValue));
    }

    private static bool TunesEqual(TuneData a, TuneData b)
    {
        return a.TargetTime == b.TargetTime
            && a.Direction == b.Direction
            && a.UseRelative == b.UseRelative
            && a.TargetLane == b.TargetLane
            && a.Duration == b.Duration
            && Mathf.Approximately(a.PosValue.x, b.PosValue.x)
            && Mathf.Approximately(a.PosValue.y, b.PosValue.y);
    }

    private static string NoteToString(NoteData n)
    {
        return $"{n.Type} Time:{n.TargetTime} Lane:{n.LaneIndex} Dur:{n.SustainDuration} Appear:{n.AppearOffset} Speed:{n.SpeedMode}({n.SpeedValue})";
    }

    [ContextMenu("Test Whitespace Tolerance")]
    private void TestWhitespaceTolerance()
    {
        Debug.Log("===== 空格容错测试 =====\n");

        string noSpace    = "Dot-Time:1000;Lane:0;Duration:0;Appear:-3000;Speed:Default()";
        string extraSpace = "Dot  -   Time : 1000  ;  Lane  :  0  ;  Duration  :  0  ;  Appear  :  -3000  ;  Speed  :  Default()";
        string mixedSpace = "Dot -  Time :1000 ;Lane:0; Duration:0;Appear:-3000;Speed:Default()";

        var r1 = ChartParser.ParseNotes(noSpace);
        var r2 = ChartParser.ParseNotes(extraSpace);
        var r3 = ChartParser.ParseNotes(mixedSpace);

        bool ok = r1.Count == 1 && r2.Count == 1 && r3.Count == 1
               && r1[0].TargetTime == r2[0].TargetTime && r2[0].TargetTime == r3[0].TargetTime
               && r1[0].LaneIndex == r2[0].LaneIndex && r2[0].LaneIndex == r3[0].LaneIndex
               && r1[0].AppearOffset == r2[0].AppearOffset && r2[0].AppearOffset == r3[0].AppearOffset;

        Debug.Log(ok ? "空格容错测试通过！三种格式解析结果一致。" : "空格容错测试失败！");
    }

    [ContextMenu("Test Error Cases")]
    private void TestErrors()
    {
        Debug.Log("===== 异常情况测试 =====\n");

        var errorCases = new (string desc, string input)[]
        {
            ("空字符串-应返回空列表", ""),
            ("缺少分隔符", "Dot Time:10000 ; Lane:0"),
            ("未知类型", "Foo - Time:10000 ; Lane:0"),
            ("未知字段", "Dot - Time:10000 ; Lane:0 ; Foo:bar"),
            ("Time非数字", "Dot - Time:abc ; Lane:0"),
            ("Lane超出范围", "Dot - Time:10000 ; Lane:99"),
            ("Appear非数字", "Dot - Time:10000 ; Lane:0 ; Appear:xyz"),
            ("Speed格式错误", "Dot - Time:10000 ; Lane:0 ; Speed:Bad"),
            ("Tune前无Dash", " - Tune - Time:10000 ; Direction:Left ; Motion:Absolute((200,0))"),
            ("Tune未知方向", "Dot - Time:10000 ; Lane:0\nDash - Time:12000 ; Lane:1\n - Tune - Time:13000 ; Direction:Up ; Motion:Absolute((200,0))"),
            ("Motion格式错误", "Dot - Time:10000 ; Lane:0\nDash - Time:12000 ; Lane:1\n - Tune - Time:13000 ; Direction:Left ; Motion:Bad"),
            ("Tune Duration非数字", "Dot - Time:10000 ; Lane:0\nDash - Time:12000 ; Lane:1\n - Tune - Time:13000 ; Direction:Left ; Motion:Absolute((200,0)) ; Duration:xyz"),
        };

        foreach (var (desc, input) in errorCases)
        {
            try
            {
                var result = ChartParser.ParseNotes(input);
                if (desc.StartsWith("空字符串"))
                    Debug.Log($"空字符串正确返回空列表 (Count={result.Count})");
                else
                    Debug.LogWarning($"未触发异常：{desc}");
            }
            catch (ChartException ex)
            {
                Debug.Log($"正确捕获 [{desc}]：{ex.Message}");
            }
            catch
            {
                Debug.LogWarning($"非预期异常类型：{desc}");
            }
        }
    }

    // ==================== LaneMotion 测试 ====================

    [ContextMenu("Test ParseLaneMotions")]
    private void TestParseLaneMotions()
    {
        Debug.Log("===== ParseLaneMotions 测试 =====\n");

        try
        {
            var motions = ChartParser.ParseLaneMotions(SampleChart);
            Debug.Log($"解析成功，共 {motions.Count} 条轨道移动：\n");

            for (int i = 0; i < motions.Count; i++)
            {
                var m = motions[i];
                string info = $"[{i}] Lane:{m.LaneIndex} | Start:{m.StartTime}ms | Dur:{m.Duration}ms | Anchor:({m.Anchor.x},{m.Anchor.y})";
                if (m.Position.HasValue)
                    info += $" | Position:[{(m.Position.Value.UseRelative ? "Relative" : "Absolute")},({m.Position.Value.Pos.x},{m.Position.Value.Pos.y}),{m.Position.Value.CurveMode}]";
                if (m.Rotation.HasValue)
                    info += $" | Rotation:[{(m.Rotation.Value.UseRelative ? "Relative" : "Absolute")},{m.Rotation.Value.Angle},{m.Rotation.Value.CurveMode}]";
                if (m.Opacity.HasValue)
                    info += $" | Opacity:[Head:{m.Opacity.Value.AffectHead},Line:{m.Opacity.Value.AffectLine},Key:{m.Opacity.Value.AffectKey},Note:{m.Opacity.Value.AffectNote},{m.Opacity.Value.Opacity},{m.Opacity.Value.CurveMode}]";
                Debug.Log(info);
            }
        }
        catch (ChartException ex)
        {
            Debug.LogError($"解析失败：{ex.Message}");
        }
    }

    [ContextMenu("Test LaneMotion RoundTrip")]
    private void TestLaneMotionRoundTrip()
    {
        Debug.Log("===== LaneMotion 双向转换测试 =====\n");

        try
        {
            var parsed1 = ChartParser.ParseLaneMotions(SampleChart);
            string serialized = ChartParser.SerializeLaneMotions(parsed1);
            var parsed2 = ChartParser.ParseLaneMotions(serialized);

            Debug.Log("序列化输出：\n" + serialized);

            bool countsMatch = parsed1.Count == parsed2.Count;
            Debug.Log($"\n行数一致：{countsMatch} ({parsed1.Count} vs {parsed2.Count})");

            bool allMatch = true;
            for (int i = 0; i < parsed1.Count && i < parsed2.Count; i++)
            {
                var a = parsed1[i];
                var b = parsed2[i];
                if (!LaneMotionsEqual(a, b))
                {
                    Debug.LogWarning($"第{i}条不匹配！");
                    allMatch = false;
                }
            }

            Debug.Log(allMatch && countsMatch ? "LaneMotion 双向转换一致！" : "存在不匹配！");
        }
        catch (ChartException ex)
        {
            Debug.LogError($"测试失败：{ex.Message}");
        }
    }

    private static bool LaneMotionsEqual(LaneMotionEffect a, LaneMotionEffect b)
    {
        if (a.LaneIndex != b.LaneIndex || a.StartTime != b.StartTime || a.Duration != b.Duration)
            return false;
        if (a.Anchor.x != b.Anchor.x || a.Anchor.y != b.Anchor.y)
            return false;
        if (!PositionsEqual(a.Position, b.Position)) return false;
        if (!RotationsEqual(a.Rotation, b.Rotation)) return false;
        if (!OpacitiesEqual(a.Opacity, b.Opacity)) return false;
        return true;
    }

    private static bool PositionsEqual(PositionMotion? a, PositionMotion? b)
    {
        if (!a.HasValue && !b.HasValue) return true;
        if (!a.HasValue || !b.HasValue) return false;
        return a.Value.UseRelative == b.Value.UseRelative
            && a.Value.Pos.x == b.Value.Pos.x && a.Value.Pos.y == b.Value.Pos.y
            && a.Value.CurveMode == b.Value.CurveMode;
    }

    private static bool RotationsEqual(RotationMotion? a, RotationMotion? b)
    {
        if (!a.HasValue && !b.HasValue) return true;
        if (!a.HasValue || !b.HasValue) return false;
        return a.Value.UseRelative == b.Value.UseRelative
            && Mathf.Approximately(a.Value.Angle, b.Value.Angle)
            && a.Value.CurveMode == b.Value.CurveMode;
    }

    private static bool OpacitiesEqual(OpacityMotion? a, OpacityMotion? b)
    {
        if (!a.HasValue && !b.HasValue) return true;
        if (!a.HasValue || !b.HasValue) return false;
        return a.Value.AffectHead == b.Value.AffectHead
            && a.Value.AffectLine == b.Value.AffectLine
            && a.Value.AffectKey == b.Value.AffectKey
            && a.Value.AffectNote == b.Value.AffectNote
            && Mathf.Approximately(a.Value.Opacity, b.Value.Opacity)
            && a.Value.CurveMode == b.Value.CurveMode;
    }

    // ==================== BPM 测试 ====================

    [ContextMenu("Test ParseBpmPoints")]
    private void TestParseBpmPoints()
    {
        Debug.Log("===== ParseBpmPoints 测试 =====\n");

        try
        {
            var points = ChartParser.ParseBpmPoints(SampleChart);
            Debug.Log($"解析成功，共 {points.Count} 个 BPM 变化点：\n");

            for (int i = 0; i < points.Count; i++)
            {
                Debug.Log($"[{i}] Time:{points[i].StartTime}ms | BPM:{points[i].Bpm}");
            }
        }
        catch (ChartException ex)
        {
            Debug.LogError($"解析失败：{ex.Message}");
        }
    }

    [ContextMenu("Test BPM RoundTrip")]
    private void TestBpmRoundTrip()
    {
        Debug.Log("===== BPM 双向转换测试 =====\n");

        try
        {
            var parsed1 = ChartParser.ParseBpmPoints(SampleChart);
            string serialized = ChartParser.SerializeBpmPoints(parsed1);
            var parsed2 = ChartParser.ParseBpmPoints(serialized);

            Debug.Log("序列化输出：\n" + serialized);

            bool countsMatch = parsed1.Count == parsed2.Count;
            Debug.Log($"\n行数一致：{countsMatch} ({parsed1.Count} vs {parsed2.Count})");

            bool allMatch = true;
            for (int i = 0; i < parsed1.Count && i < parsed2.Count; i++)
            {
                if (parsed1[i].StartTime != parsed2[i].StartTime || !Mathf.Approximately(parsed1[i].Bpm, parsed2[i].Bpm))
                {
                    Debug.LogWarning($"第{i}条不匹配！P1: Time:{parsed1[i].StartTime} BPM:{parsed1[i].Bpm} / P2: Time:{parsed2[i].StartTime} BPM:{parsed2[i].Bpm}");
                    allMatch = false;
                }
            }

            Debug.Log(allMatch && countsMatch ? "BPM 双向转换一致！" : "存在不匹配！");
        }
        catch (ChartException ex)
        {
            Debug.LogError($"测试失败：{ex.Message}");
        }
    }
}
