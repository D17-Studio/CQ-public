using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CQMusicGame.Shared
{
    public static class ChartParser
    {
        private static readonly string[] LineSeparators = { "\r\n", "\n", "\r" };
        private static readonly string[] KnownTypes = { "LaneMotion", "Dash", "Dot", "Mute", "Tune", "BPM" };

        #region Note —— 序列化 / 反序列化

        public static string SerializeNotes(List<NoteData> notes)
        {
            string result = "";

            foreach (var note in notes)
            {
                string speed = FormatSpeed(note);
                string typeName = note.Type switch
                {
                    NoteType.Dot => "Dot",
                    NoteType.Dash => "Dash",
                    NoteType.Mute => "Mute",
                    _ => ""
                };
                result += $"{typeName} - Time:{note.TargetTime} ; Lane:{note.LaneIndex} ; Duration:{note.SustainDuration} ; Appear:{note.AppearOffset} ; Speed:{speed}\n";

                if (note.Type == NoteType.Dash && note.LinkedTunes != null)
                {
                    foreach (var tune in note.LinkedTunes)
                    {
                        string motion = tune.UseRelative
                            ? $"Relative({tune.TargetLane},({FormatFloat(tune.PosValue.x)},{FormatFloat(tune.PosValue.y)}))"
                            : $"Absolute(({FormatFloat(tune.PosValue.x)},{FormatFloat(tune.PosValue.y)}))";
                        result += $" - Tune - Time:{tune.TargetTime} ; Direction:{tune.Direction} ; Motion:{motion} ; Duration:{tune.Duration}\n";
                    }
                }
            }

            return result;
        }

        public static List<NoteData> ParseNotes(string chart)
        {
            if (string.IsNullOrWhiteSpace(chart))
                return new List<NoteData>();

            try { return ParseNotesUnsafe(chart); }
            catch (ChartException) { throw; }
            catch (Exception ex) { throw new ChartException($"谱面解析失败：{ex.Message}"); }
        }

        private static List<NoteData> ParseNotesUnsafe(string chart)
        {
            var notes = new List<NoteData>();
            var lines = chart.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
            bool hasLastDash = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                int lineNum = i + 1;
                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith('-'))  //  - Tune - ...
                {
                    if (!hasLastDash)
                        throw new ChartException($"第{lineNum}行：Tune出现在Dash之前");

                    var (typeName, fieldsText) = SplitTypeAndFields(line, lineNum);
                    if (typeName != "Tune")
                        throw new ChartException($"第{lineNum}行：缩进行不是Tune");

                    TuneData tune = ParseTuneFields(fieldsText, lineNum);
                    var dash = notes[^1];
                    dash.LinkedTunes ??= Array.Empty<TuneData>();
                    Array.Resize(ref dash.LinkedTunes, dash.LinkedTunes.Length + 1);
                    dash.LinkedTunes[^1] = tune;
                    notes[^1] = dash;
                }
                else  // Type - ...
                {
                    var (typeName, fieldsText) = SplitTypeAndFields(line, lineNum);
                    if (typeName != "Dot" && typeName != "Dash" && typeName != "Mute")
                        continue;  // 不是Note行，跳过（可能是LaneMotion行）

                    NoteData note = ParseNoteFields(typeName, fieldsText, lineNum);
                    notes.Add(note);
                    hasLastDash = note.Type == NoteType.Dash;
                }
            }

            return notes;
        }

        /// <summary> 按已知类型名 + 可选空格 + "-" + 可选空格 拆分 </summary>
        private static (string type, string fields) SplitTypeAndFields(string line, int lineNum)
        {
            line = line.TrimStart();

            // Tune 子行：去掉开头的 "-"
            if (line.StartsWith('-'))
                line = line.Substring(1).TrimStart();

            // 匹配已知类型名，后面紧跟的可选空格和 "-"
            foreach (var typeName in KnownTypes)
            {
                if (!line.StartsWith(typeName, StringComparison.Ordinal))
                    continue;

                int afterType = typeName.Length;
                // 跳过类型名后的空格
                while (afterType < line.Length && line[afterType] == ' ')
                    afterType++;

                if (afterType >= line.Length || line[afterType] != '-')
                    continue;

                int sepEnd = afterType + 1; // 跳过 "-"
                string fields = line.Substring(sepEnd).TrimStart();
                return (typeName, fields);
            }

            throw new ChartException($"第{lineNum}行：无法识别行的类型");
        }

        #endregion

        #region Note 字段解析

        private static NoteData ParseNoteFields(string typeName, string fieldsText, int lineNum)
        {
            var note = new NoteData();
            note.Type = typeName switch
            {
                "Dot" => NoteType.Dot,
                "Dash" => NoteType.Dash,
                "Mute" => NoteType.Mute,
                _ => throw new ChartException($"第{lineNum}行：未知的Note类型 \"{typeName}\"")
            };

            foreach (var (key, value) in SplitFields(fieldsText))
            {
                switch (key)
                {
                    case "Time":    note.TargetTime = ReadInt(value, lineNum, "Time"); break;
                    case "Lane":    note.LaneIndex = ReadLaneIndex(value, lineNum); break;
                    case "Duration": note.SustainDuration = ReadInt(value, lineNum, "Duration"); break;
                    case "Appear":   note.AppearOffset = ReadInt(value, lineNum, "Appear"); break;
                    case "Speed":   ReadSpeed(value, ref note, lineNum); break;
                    default:        throw new ChartException($"第{lineNum}行：未知字段 \"{key}\"");
                }
            }
            return note;
        }

        private static TuneData ParseTuneFields(string fieldsText, int lineNum)
        {
            var tune = new TuneData();
            foreach (var (key, value) in SplitFields(fieldsText))
            {
                switch (key)
                {
                    case "Time":      tune.TargetTime = ReadInt(value, lineNum, "Time"); break;
                    case "Direction": tune.Direction = ReadDirection(value, lineNum); break;
                    case "Motion":    ReadMotion(value, ref tune, lineNum); break;
                    case "Duration":  tune.Duration = ReadInt(value, lineNum, "Duration"); break;
                    default:          throw new ChartException($"第{lineNum}行：Tune未知字段 \"{key}\"");
                }
            }
            return tune;
        }

        private static TuneDirection ReadDirection(string value, int lineNum) => value switch
        {
            "Left" => TuneDirection.Left,
            "Right" => TuneDirection.Right,
            _ => throw new ChartException($"第{lineNum}行：未知的Tune方向 \"{value}\"")
        };

        private static void ReadMotion(string value, ref TuneData tune, int lineNum)
        {
            var (mode, parts) = ParseBracketArgs(value, lineNum, "Motion");
            switch (mode)
            {
                case "Relative":
                    if (parts.Length != 2) throw new ChartException($"第{lineNum}行：Relative需要两个参数(lane, offset)");
                    tune.UseRelative = true;
                    tune.TargetLane = ReadInt(parts[0].Trim(), lineNum, "TargetLane");
                    tune.PosValue = ReadVec2(parts[1].Trim(), lineNum, "Offset");
                    break;
                case "Absolute":
                    if (parts.Length != 1) throw new ChartException($"第{lineNum}行：Absolute需要一个参数(pos)");
                    tune.UseRelative = false;
                    tune.PosValue = ReadVec2(parts[0].Trim(), lineNum, "Pos");
                    break;
                default:
                    throw new ChartException($"第{lineNum}行：未知的Motion模式 \"{mode}\"");
            }
        }

        private static void ReadSpeed(string value, ref NoteData note, int lineNum)
        {
            var (mode, parts) = ParseBracketArgs(value, lineNum, "Speed");
            switch (mode)
            {
                case "Default":    note.SpeedMode = NoteSpeedMode.Default; note.SpeedValue = 1f; break;
                case "Multiplier":
                    if (parts.Length != 1) throw new ChartException($"第{lineNum}行：Multiplier需要一个参数");
                    note.SpeedMode = NoteSpeedMode.Multiplier;
                    note.SpeedValue = ReadFloat(parts[0].Trim(), lineNum, "Multiplier值");
                    break;
                case "Fixed":
                    if (parts.Length != 1) throw new ChartException($"第{lineNum}行：Fixed需要一个参数");
                    note.SpeedMode = NoteSpeedMode.Fixed;
                    note.SpeedValue = ReadFloat(parts[0].Trim(), lineNum, "Fixed值");
                    break;
                default:
                    throw new ChartException($"第{lineNum}行：未知的Speed模式 \"{mode}\"");
            }
        }

        #endregion

        #region LaneMotionEffect —— 序列化 / 反序列化

        public static string SerializeLaneMotions(List<LaneMotionEffect> motions)
        {
            string result = "";
            foreach (var m in motions)
            {
                result += $"LaneMotion - Time:{m.StartTime} ; Lane:{m.LaneIndex} ; Duration:{m.Duration} ; Anchor:({FormatFloat(m.Anchor.x)},{FormatFloat(m.Anchor.y)})";
                if (m.Position.HasValue) result += $" ; Position:{FormatMotion(m.Position.Value)}";
                if (m.Rotation.HasValue) result += $" ; Rotation:{FormatMotion(m.Rotation.Value)}";
                if (m.Opacity.HasValue) result += $" ; Opacity:{FormatMotion(m.Opacity.Value)}";
                result += "\n";
            }
            return result;
        }

        public static List<LaneMotionEffect> ParseLaneMotions(string chart)
        {
            var list = new List<LaneMotionEffect>();
            if (string.IsNullOrWhiteSpace(chart)) return list;

            var lines = chart.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                int lineNum = i + 1;
                if (string.IsNullOrEmpty(line)) continue;

                // 跳过 Note 行
                string firstWord = line.TrimStart().Split(' ')[0];
                if (firstWord == "Dot" || firstWord == "Dash" || firstWord == "Mute" || firstWord == "Tune" || firstWord.StartsWith('-'))
                    continue;

                if (firstWord == "BPM") continue;

                if (firstWord == "LaneMotion")
                {
                    var (_, fields) = SplitTypeAndFields(line, lineNum);
                    list.Add(ParseLaneMotionFields(fields, lineNum));
                }
                else
                    throw new ChartException($"第{lineNum}行：无法识别的行 \"{line}\"");
            }
            return list;
        }

        #endregion

        #region BPM —— 序列化 / 反序列化

        public static string SerializeBpmPoints(List<BpmPoint> points)
        {
            string result = "";
            foreach (var p in points)
                result += $"BPM - Time:{p.StartTime} ; BPM:{FormatFloat(p.Bpm)}\n";
            return result;
        }

        public static List<BpmPoint> ParseBpmPoints(string chart)
        {
            var list = new List<BpmPoint>();
            if (string.IsNullOrWhiteSpace(chart)) return list;

            var lines = chart.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                int lineNum = i + 1;
                if (string.IsNullOrEmpty(line)) continue;

                string firstWord = line.TrimStart().Split(' ')[0];
                if (firstWord != "BPM") continue;

                var (_, fields) = SplitTypeAndFields(line, lineNum);
                list.Add(ParseBpmFields(fields, lineNum));
            }
            return list;
        }

        private static BpmPoint ParseBpmFields(string fieldsText, int lineNum)
        {
            var point = new BpmPoint();
            foreach (var (key, value) in SplitFields(fieldsText))
            {
                switch (key)
                {
                    case "Time": point.StartTime = ReadInt(value, lineNum, "Time"); break;
                    case "BPM":  point.Bpm = ReadFloat(value, lineNum, "BPM"); break;
                    default:     throw new ChartException($"第{lineNum}行：BPM未知字段 \"{key}\"");
                }
            }
            return point;
        }

        #endregion

        #region LaneMotionEffect 字段解析

        private static LaneMotionEffect ParseLaneMotionFields(string fieldsText, int lineNum)
        {
            var effect = new LaneMotionEffect();
            foreach (var (key, value) in SplitFields(fieldsText))
            {
                switch (key)
                {
                    case "Time":     effect.StartTime = ReadInt(value, lineNum, "Time"); break;
                    case "Lane":     effect.LaneIndex = ReadInt(value, lineNum, "Lane"); break;
                    case "Duration": effect.Duration = ReadInt(value, lineNum, "Duration"); break;
                    case "Anchor":   effect.Anchor = ReadVec2(value, lineNum, "Anchor"); break;
                    case "Position": effect.Position = ReadPositionMotion(value, lineNum); break;
                    case "Rotation": effect.Rotation = ReadRotationMotion(value, lineNum); break;
                    case "Opacity":  effect.Opacity = ReadOpacityMotion(value, lineNum); break;
                    default:         throw new ChartException($"第{lineNum}行：轨道移动未知字段 \"{key}\"");
                }
            }
            return effect;
        }

        private static PositionMotion ReadPositionMotion(string value, int lineNum)
        {
            var (mode, args) = ParseSquareBracketArgs(value, lineNum, "Position");
            if (args.Length < 2) throw new ChartException($"第{lineNum}行：Position需要(位置, 曲线)");
            return new PositionMotion
            {
                UseRelative = mode == "Relative",
                Pos = ReadVec2(args[0].Trim(), lineNum, "Position位置"),
                CurveMode = ReadEaseCurve(args[1].Trim(), lineNum)
            };
        }

        private static RotationMotion ReadRotationMotion(string value, int lineNum)
        {
            var (mode, args) = ParseSquareBracketArgs(value, lineNum, "Rotation");
            if (args.Length < 2) throw new ChartException($"第{lineNum}行：Rotation需要(角度, 曲线)");
            return new RotationMotion
            {
                UseRelative = mode == "Relative",
                Angle = ReadFloat(args[0].Trim(), lineNum, "Rotation角度"),
                CurveMode = ReadEaseCurve(args[1].Trim(), lineNum)
            };
        }

        private static OpacityMotion ReadOpacityMotion(string value, int lineNum)
        {
            var (mode, args) = ParseSquareBracketArgs(value, lineNum, "Opacity");
            if (mode != "Affect" || args.Length < 2)
                throw new ChartException($"第{lineNum}行：Opacity格式错误，应为 [Affect,(部位),值,曲线]");

            var motion = new OpacityMotion();
            string affectList = args[0].Trim().Trim('(', ')');
            foreach (var p in affectList.Split(','))
            {
                switch (p.Trim())
                {
                    case "Head": motion.AffectHead = true; break;
                    case "Line": motion.AffectLine = true; break;
                    case "Key":  motion.AffectKey = true; break;
                    case "Note": motion.AffectNote = true; break;
                }
            }
            motion.Opacity = ReadFloat(args[1].Trim(), lineNum, "Opacity值");
            motion.CurveMode = ReadEaseCurve(args[2].Trim(), lineNum);
            return motion;
        }

        private static EaseCurve ReadEaseCurve(string value, int lineNum)
            => Enum.TryParse<EaseCurve>(value, out var curve)
                ? curve
                : throw new ChartException($"第{lineNum}行：未知的缓动曲线 \"{value}\"");

        private static Vec2 ReadVec2(string value, int lineNum, string fieldName)
        {
            int start = value.IndexOf('('), end = value.LastIndexOf(')');
            if (start < 0 || end < 0 || end <= start)
                throw new ChartException($"第{lineNum}行：{fieldName}格式错误，缺少括号 \"{value}\"");

            string[] parts = value.Substring(start + 1, end - start - 1).Split(',');
            if (parts.Length != 2)
                throw new ChartException($"第{lineNum}行：{fieldName}需要两个参数(x,y)");

            return new Vec2(ReadFloat(parts[0].Trim(), lineNum, fieldName + " x"),
                            ReadFloat(parts[1].Trim(), lineNum, fieldName + " y"));
        }

        #endregion

        #region 轨道/旋转/不透明度 Motion 序列化

        private static string FormatMotion(PositionMotion m)
            => m.UseRelative
                ? $"[Relative,({FormatFloat(m.Pos.x)},{FormatFloat(m.Pos.y)}),{m.CurveMode}]"
                : $"[Absolute,({FormatFloat(m.Pos.x)},{FormatFloat(m.Pos.y)}),{m.CurveMode}]";

        private static string FormatMotion(RotationMotion m)
            => m.UseRelative
                ? $"[Relative,{FormatFloat(m.Angle)},{m.CurveMode}]"
                : $"[Absolute,{FormatFloat(m.Angle)},{m.CurveMode}]";

        private static string FormatMotion(OpacityMotion m)
        {
            string parts = "";
            if (m.AffectHead) parts += "Head,";
            if (m.AffectLine) parts += "Line,";
            if (m.AffectKey) parts += "Key,";
            if (m.AffectNote) parts += "Note,";
            return $"[Affect,({parts.TrimEnd(',')}),{FormatFloat(m.Opacity)},{m.CurveMode}]";
        }

        #endregion

        #region 通用工具

        /// <summary> 按 ";" 分隔字段，按 ":" 分隔键值，忽略空格 </summary>
        private static IEnumerable<(string key, string value)> SplitFields(string inner)
        {
            foreach (string field in inner.Split(';'))
            {
                string trimmed = field.Trim();
                if (trimmed.Length == 0) continue;

                int colonIdx = trimmed.IndexOf(':');
                if (colonIdx < 0)
                    throw new ChartException($"字段格式错误 \"{trimmed}\"（缺少 \":\"）");

                string key = trimmed.Substring(0, colonIdx).TrimEnd();
                string value = trimmed.Substring(colonIdx + 1).TrimStart();
                yield return (key, value);
            }
        }

        /// <summary> 提取括号参数：Absolute(200) → ("Absolute", ["200"]) </summary>
        private static (string mode, string[] args) ParseBracketArgs(string value, int lineNum, string fieldName)
        {
            int start = value.IndexOf('('), end = value.LastIndexOf(')');
            if (start < 0 || end < 0 || end <= start)
                throw new ChartException($"第{lineNum}行：{fieldName}格式错误，缺少括号 \"{value}\"");

            string mode = value.Substring(0, start);
            string argsStr = value.Substring(start + 1, end - start - 1);
            string[] args = argsStr.Length == 0 ? Array.Empty<string>() : SplitSmart(argsStr);
            return (mode, args);
        }

        /// <summary> 提取方括号参数，智能处理嵌套括号： [Absolute,(200,-256),OutCubic] </summary>
        private static (string mode, string[] args) ParseSquareBracketArgs(string value, int lineNum, string fieldName)
        {
            int start = value.IndexOf('['), end = value.LastIndexOf(']');
            if (start < 0 || end < 0 || end <= start)
                throw new ChartException($"第{lineNum}行：{fieldName}格式错误，缺少方括号 \"{value}\"");

            string inner = value.Substring(start + 1, end - start - 1);
            int firstComma = FirstTopLevelComma(inner);
            string mode = firstComma >= 0 ? inner.Substring(0, firstComma) : inner;
            string argsStr = firstComma >= 0 ? inner.Substring(firstComma + 1) : "";
            return (mode.Trim(), SplitSmart(argsStr));
        }

        private static int FirstTopLevelComma(string input)
        {
            int depth = 0;
            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == '(' || input[i] == '[') depth++;
                else if (input[i] == ')' || input[i] == ']') depth--;
                else if (input[i] == ',' && depth == 0) return i;
            }
            return -1;
        }

        /// <summary> 按逗号分割，但不分割括号内的逗号 </summary>
        private static string[] SplitSmart(string input)
        {
            var result = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == '(' || input[i] == '[') depth++;
                else if (input[i] == ')' || input[i] == ']') depth--;
                else if (input[i] == ',' && depth == 0)
                {
                    result.Add(input.Substring(start, i - start));
                    start = i + 1;
                }
            }
            result.Add(input.Substring(start));
            return result.ToArray();
        }

        private static int ReadInt(string value, int lineNum, string fieldName)
        {
            if (int.TryParse(value, out int result)) return result;
            throw new ChartException($"第{lineNum}行：{fieldName}值 \"{value}\" 不是有效整数");
        }

        private static int ReadLaneIndex(string value, int lineNum)
        {
            int idx = ReadInt(value, lineNum, "Lane");
            if (idx < -3 || idx > 3)
                throw new ChartException($"第{lineNum}行：轨道序号 {idx} 超出范围[-3, 3]");
            return idx;
        }

        private static float ReadFloat(string value, int lineNum, string fieldName)
        {
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)) return result;
            throw new ChartException($"第{lineNum}行：{fieldName}值 \"{value}\" 不是有效数字");
        }

        private static string FormatSpeed(NoteData note)
            => note.SpeedMode switch
            {
                NoteSpeedMode.Multiplier => $"Multiplier({FormatFloat(note.SpeedValue)})",
                NoteSpeedMode.Fixed => $"Fixed({FormatFloat(note.SpeedValue)})",
                _ => "Default()"
            };

        private static string FormatFloat(float value)
            => value.ToString("0.#####", CultureInfo.InvariantCulture);

        #endregion
    }
}