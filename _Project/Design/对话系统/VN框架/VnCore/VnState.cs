using System;
using System.Collections.Generic;

namespace CQ.Vn
{
    public sealed class VnLine
    {
        public VnLine(string speakerId, string speakerName, string speakerColor, string text)
        {
            SpeakerId = speakerId;
            SpeakerName = speakerName;
            SpeakerColor = speakerColor;
            Text = text ?? string.Empty;
        }

        public string SpeakerId { get; }
        public string SpeakerName { get; }
        public string SpeakerColor { get; }
        public string Text { get; }
        public bool IsNarration => string.IsNullOrEmpty(SpeakerId);

        public static VnLine FromData(VnLineData data)
        {
            if (data == null)
            {
                return null;
            }

            return new VnLine(data.SpeakerId, data.SpeakerName, data.SpeakerColor, data.Text);
        }
    }

    public sealed class VnSceneCharacter
    {
        public VnSceneCharacter(string id, string displayName, string color, string position, string expression)
        {
            Id = id;
            DisplayName = displayName;
            Color = color;
            Position = position;
            Expression = expression;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Color { get; }
        public string Position { get; }
        public string Expression { get; }
    }

    public sealed class VnChoiceOption
    {
        public VnChoiceOption(string text, string targetLabel)
        {
            Text = text;
            TargetLabel = targetLabel;
        }

        public string Text { get; }
        public string TargetLabel { get; }
    }

    public sealed class VnHistoryEntry
    {
        public VnHistoryEntry(string speakerId, string speakerName, string speakerColor, string text, bool isChoice)
        {
            SpeakerId = speakerId;
            SpeakerName = speakerName;
            SpeakerColor = speakerColor;
            Text = text ?? string.Empty;
            IsChoice = isChoice;
        }

        public string SpeakerId { get; }
        public string SpeakerName { get; }
        public string SpeakerColor { get; }
        public string Text { get; }
        public bool IsChoice { get; }

        public static VnHistoryEntry FromLine(VnLine line) =>
            new VnHistoryEntry(line.SpeakerId, line.SpeakerName, line.SpeakerColor, line.Text, false);

        public static VnHistoryEntry FromChoice(string text) =>
            new VnHistoryEntry(null, null, "#b8c4ff", text, true);

        public static VnHistoryEntry FromData(VnLineData data) =>
            new VnHistoryEntry(data.SpeakerId, data.SpeakerName, data.SpeakerColor, data.Text, data.IsChoice);
    }

    public enum VnStepKind
    {
        Line,
        Choice,
        Ended
    }

    public sealed class VnStepResult
    {
        private VnStepResult(VnStepKind kind, VnLine line, IReadOnlyList<VnChoiceOption> choices)
        {
            Kind = kind;
            Line = line;
            Choices = choices ?? Array.Empty<VnChoiceOption>();
        }

        public VnStepKind Kind { get; }
        public VnLine Line { get; }
        public IReadOnlyList<VnChoiceOption> Choices { get; }

        public static VnStepResult ForLine(VnLine line) =>
            new VnStepResult(VnStepKind.Line, line, null);

        public static VnStepResult ForChoice(VnLine line, IReadOnlyList<VnChoiceOption> choices) =>
            new VnStepResult(VnStepKind.Choice, line, choices);

        public static VnStepResult ForEnd(VnLine line) =>
            new VnStepResult(VnStepKind.Ended, line, null);
    }

    public sealed class VnValueData
    {
        public string Kind { get; set; } = nameof(VnValueKind.Integer);
        public double Number { get; set; }
        public bool Bool { get; set; }
        public string Text { get; set; } = string.Empty;

        public static VnValueData FromValue(VnValue value)
        {
            return new VnValueData
            {
                Kind = value.Kind.ToString(),
                Number = value.Number,
                Bool = value.Bool,
                Text = value.Text ?? string.Empty
            };
        }

        public VnValue ToValue()
        {
            if (!Enum.TryParse(Kind, true, out VnValueKind kind))
            {
                kind = VnValueKind.Integer;
            }

            switch (kind)
            {
                case VnValueKind.Float:
                    return VnValue.FromFloat(Number);
                case VnValueKind.Boolean:
                    return VnValue.FromBoolean(Bool);
                case VnValueKind.String:
                    return VnValue.FromString(Text);
                default:
                    return VnValue.FromInteger((long)Number);
            }
        }
    }

    public sealed class VnCharacterStateData
    {
        public string Id { get; set; }
        public string Position { get; set; }
        public string Expression { get; set; }
    }

    public sealed class VnSceneStateData
    {
        public string Background { get; set; }
        public string Music { get; set; }
        public List<VnCharacterStateData> Characters { get; set; } = new List<VnCharacterStateData>();
    }

    public sealed class VnLineData
    {
        public string SpeakerId { get; set; }
        public string SpeakerName { get; set; }
        public string SpeakerColor { get; set; }
        public string Text { get; set; }
        public bool IsChoice { get; set; }
    }

    public sealed class VnSnapshot
    {
        public int ProgramCounter { get; set; }
        public List<int> CallStack { get; set; } = new List<int>();
        public Dictionary<string, VnValueData> Variables { get; set; } = new Dictionary<string, VnValueData>(StringComparer.Ordinal);
        public VnSceneStateData Scene { get; set; } = new VnSceneStateData();
        public List<VnLineData> History { get; set; } = new List<VnLineData>();
        public VnLineData LastLine { get; set; }
        public bool Finished { get; set; }
    }
}
