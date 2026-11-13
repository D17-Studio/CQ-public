using System;
using System.Collections.Generic;

namespace CQ.Vn
{
    public abstract class VnInstruction
    {
        protected VnInstruction(int sourceLine)
        {
            SourceLine = sourceLine;
        }

        public int SourceLine { get; }
    }

    public sealed class SayInstruction : VnInstruction
    {
        public SayInstruction(int sourceLine, string speakerId, string text)
            : base(sourceLine)
        {
            SpeakerId = string.IsNullOrWhiteSpace(speakerId) ? null : speakerId.Trim();
            Text = text ?? string.Empty;
        }

        public string SpeakerId { get; }
        public string Text { get; }
        public bool IsNarration => SpeakerId == null;
    }

    public sealed class BackgroundInstruction : VnInstruction
    {
        public BackgroundInstruction(int sourceLine, string backgroundId)
            : base(sourceLine)
        {
            BackgroundId = backgroundId;
        }

        public string BackgroundId { get; }
    }

    public sealed class ShowCharacterInstruction : VnInstruction
    {
        public ShowCharacterInstruction(int sourceLine, string characterId, string position, string expression)
            : base(sourceLine)
        {
            CharacterId = characterId;
            Position = position;
            Expression = expression;
        }

        public string CharacterId { get; }
        public string Position { get; }
        public string Expression { get; }
    }

    public sealed class HideCharacterInstruction : VnInstruction
    {
        public HideCharacterInstruction(int sourceLine, string characterId)
            : base(sourceLine)
        {
            CharacterId = characterId;
        }

        public string CharacterId { get; }
    }

    public sealed class MusicInstruction : VnInstruction
    {
        public MusicInstruction(int sourceLine, string trackId)
            : base(sourceLine)
        {
            TrackId = trackId;
        }

        public string TrackId { get; }
    }

    public enum VnSetOperator
    {
        Assign,
        Add,
        Subtract
    }

    public sealed class SetInstruction : VnInstruction
    {
        public SetInstruction(int sourceLine, string variable, VnSetOperator setOperator, VnOperand value)
            : base(sourceLine)
        {
            Variable = variable;
            Operator = setOperator;
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public string Variable { get; }
        public VnSetOperator Operator { get; }
        public VnOperand Value { get; }
    }

    public sealed class JumpInstruction : VnInstruction
    {
        public JumpInstruction(int sourceLine, string label)
            : base(sourceLine)
        {
            Label = label;
        }

        public string Label { get; }
    }

    public sealed class CallInstruction : VnInstruction
    {
        public CallInstruction(int sourceLine, string label)
            : base(sourceLine)
        {
            Label = label;
        }

        public string Label { get; }
    }

    public sealed class ReturnInstruction : VnInstruction
    {
        public ReturnInstruction(int sourceLine) : base(sourceLine)
        {
        }
    }

    public sealed class ConditionalJumpInstruction : VnInstruction
    {
        public ConditionalJumpInstruction(int sourceLine, VnCondition condition)
            : base(sourceLine)
        {
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        }

        public VnCondition Condition { get; }
        public int TargetIndex { get; internal set; } = -1;
    }

    public sealed class JumpToInstruction : VnInstruction
    {
        public JumpToInstruction(int sourceLine) : base(sourceLine)
        {
        }

        public int TargetIndex { get; internal set; } = -1;
    }

    public sealed class ChoiceInstruction : VnInstruction
    {
        public ChoiceInstruction(int sourceLine, IReadOnlyList<VnChoiceDefinition> options)
            : base(sourceLine)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public IReadOnlyList<VnChoiceDefinition> Options { get; }
    }

    public sealed class VnChoiceDefinition
    {
        public VnChoiceDefinition(string text, string targetLabel, VnCondition condition)
        {
            Text = text;
            TargetLabel = targetLabel;
            Condition = condition;
        }

        public string Text { get; }
        public string TargetLabel { get; }
        public VnCondition Condition { get; }
    }

    public sealed class EndInstruction : VnInstruction
    {
        public EndInstruction(int sourceLine) : base(sourceLine)
        {
        }
    }
}
