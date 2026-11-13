using CQ.Vn;

namespace CQ.Vn.Host;

public sealed record StartRequest(string? Label);

public sealed record ChooseRequest(int Index);

public sealed record JumpRequest(string Label);

public sealed record LoadRequest(VnSnapshot Snapshot);

public sealed record LineDto(string? SpeakerId, string SpeakerName, string SpeakerColor, string Text);

public sealed record CharacterDto(string Id, string Name, string Color, string Position, string Expression);

public sealed record SceneDto(string? Background, string? Music, IReadOnlyList<CharacterDto> Characters);

public sealed record ChoiceDto(string Text);

public sealed record HistoryDto(string? SpeakerId, string SpeakerName, string SpeakerColor, string Text, bool IsChoice);

public sealed record StateDto(
    string Kind,
    LineDto? Line,
    IReadOnlyList<ChoiceDto> Choices,
    SceneDto Scene,
    IReadOnlyDictionary<string, object> Variables,
    IReadOnlyList<HistoryDto> History,
    bool Finished,
    int ProgramCounter);

public sealed record BootstrapDto(
    string Title,
    IReadOnlyList<CharacterDto> Characters,
    IReadOnlyList<string> Labels,
    string AssetDirectory,
    int InstructionCount);

public sealed class VnSession
{
    private readonly object _gate = new();
    private readonly StoryBundle _story;
    private readonly VnMachine _machine;

    public VnSession(StoryBundle story)
    {
        _story = story;
        _machine = new VnMachine(story.Program, story.Characters);
        _machine.Start();
    }

    public BootstrapDto Bootstrap()
    {
        lock (_gate)
        {
            return new BootstrapDto(
                _story.Title,
                _story.Characters.Characters.Values
                    .Select(character => new CharacterDto(
                        character.Id,
                        character.DisplayName,
                        character.Color,
                        "center",
                        character.DefaultExpression))
                    .ToList(),
                _story.Program.Labels.Keys
                    .OrderBy(label => label, StringComparer.Ordinal)
                    .ToList(),
                _story.AssetDirectory,
                _story.Program.Instructions.Count);
        }
    }

    public StateDto Start(string? label = null)
    {
        lock (_gate)
        {
            _machine.Start(label);
            return BuildState();
        }
    }

    public StateDto Advance()
    {
        lock (_gate)
        {
            _machine.Advance();
            return BuildState();
        }
    }

    public StateDto Choose(int index)
    {
        lock (_gate)
        {
            _machine.Choose(index);
            return BuildState();
        }
    }

    public StateDto JumpTo(string label)
    {
        lock (_gate)
        {
            _machine.JumpTo(label);
            return BuildState();
        }
    }

    public VnSnapshot Save()
    {
        lock (_gate)
        {
            return _machine.ExportSnapshot();
        }
    }

    public StateDto Load(VnSnapshot snapshot)
    {
        lock (_gate)
        {
            _machine.ImportSnapshot(snapshot);

            if (!_machine.IsFinished && !_machine.IsAwaitingChoice && _machine.CurrentLine == null)
            {
                _machine.Advance();
            }

            return BuildState();
        }
    }

    private StateDto BuildState()
    {
        string kind = _machine.IsFinished
            ? "ended"
            : _machine.CurrentChoices.Count > 0
                ? "choice"
                : "line";

        LineDto? line = _machine.CurrentLine == null
            ? null
            : new LineDto(
                _machine.CurrentLine.SpeakerId,
                _machine.CurrentLine.SpeakerName,
                _machine.CurrentLine.SpeakerColor,
                _machine.CurrentLine.Text);

        List<ChoiceDto> choices = _machine.CurrentChoices
            .Select(choice => new ChoiceDto(choice.Text))
            .ToList();

        var scene = new SceneDto(
            _machine.BackgroundId,
            _machine.MusicTrackId,
            _machine.VisibleCharacters
                .Select(character => new CharacterDto(
                    character.Id,
                    character.DisplayName,
                    character.Color,
                    character.Position,
                    character.Expression))
                .ToList());

        List<HistoryDto> history = _machine.History
            .Select(entry => new HistoryDto(
                entry.SpeakerId,
                entry.SpeakerName,
                entry.SpeakerColor,
                entry.Text,
                entry.IsChoice))
            .ToList();

        return new StateDto(
            kind,
            line,
            choices,
            scene,
            _machine.GetVariableView(),
            history,
            _machine.IsFinished,
            _machine.ProgramCounter);
    }
}
