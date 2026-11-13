using System;
using System.Collections.Generic;
using System.Linq;

namespace CQ.Vn
{
    public sealed class VnMachine
    {
        private const int MaxStepsPerAdvance = 10000;

        private readonly VnProgram _program;
        private readonly VnCharacterTable _characters;
        private readonly Dictionary<string, VnValue> _variables = new Dictionary<string, VnValue>(StringComparer.Ordinal);
        private readonly List<int> _callStack = new List<int>();
        private readonly List<VnHistoryEntry> _history = new List<VnHistoryEntry>();
        private readonly List<VnSceneCharacter> _visibleCharacters = new List<VnSceneCharacter>();

        private int _programCounter;
        private IReadOnlyList<VnChoiceOption> _currentChoices = Array.Empty<VnChoiceOption>();
        private string _backgroundId;
        private string _musicTrackId;

        public VnMachine(VnProgram program, VnCharacterTable characters)
        {
            _program = program ?? throw new ArgumentNullException(nameof(program));
            _characters = characters ?? VnCharacterTable.Empty;
        }

        public VnProgram Program => _program;
        public VnCharacterTable Characters => _characters;
        public VnLine CurrentLine { get; private set; }
        public IReadOnlyList<VnChoiceOption> CurrentChoices => _currentChoices;
        public bool IsFinished { get; private set; }
        public bool IsAwaitingChoice => _currentChoices.Count > 0;
        public int ProgramCounter => _programCounter;
        public string BackgroundId => _backgroundId;
        public string MusicTrackId => _musicTrackId;
        public IReadOnlyList<VnSceneCharacter> VisibleCharacters => _visibleCharacters;
        public IReadOnlyList<VnHistoryEntry> History => _history;
        public IReadOnlyDictionary<string, VnValue> Variables => _variables;

        public VnStepResult Start(string label = null)
        {
            Reset();
            if (!string.IsNullOrEmpty(label))
            {
                if (!_program.TryGetLabel(label, out _programCounter))
                {
                    throw new ArgumentException($"未知标签「{label}」。", nameof(label));
                }
            }
            else
            {
                _programCounter = _program.EntryIndex;
            }

            return Execute();
        }

        public VnStepResult Advance()
        {
            if (IsFinished)
            {
                return VnStepResult.ForEnd(CurrentLine);
            }

            if (IsAwaitingChoice)
            {
                throw new InvalidOperationException("当前正在等待选择，请先调用 Choose。");
            }

            return Execute();
        }

        public VnStepResult Choose(int index)
        {
            if (_currentChoices.Count == 0)
            {
                throw new InvalidOperationException("当前没有等待中的选项。");
            }

            if (index < 0 || index >= _currentChoices.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            VnChoiceOption option = _currentChoices[index];
            _history.Add(VnHistoryEntry.FromChoice(option.Text));
            _currentChoices = Array.Empty<VnChoiceOption>();

            if (!_program.TryGetLabel(option.TargetLabel, out _programCounter))
            {
                throw new VnRuntimeException($"选项目标标签「{option.TargetLabel}」不存在。");
            }

            return Execute();
        }

        public VnStepResult JumpTo(string label)
        {
            if (!_program.TryGetLabel(label, out int target))
            {
                throw new ArgumentException($"未知标签「{label}」。", nameof(label));
            }

            _currentChoices = Array.Empty<VnChoiceOption>();
            IsFinished = false;
            _programCounter = target;
            return Execute();
        }

        public Dictionary<string, object> GetVariableView()
        {
            var view = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, VnValue> pair in _variables)
            {
                switch (pair.Value.Kind)
                {
                    case VnValueKind.Integer:
                        view[pair.Key] = (long)pair.Value.Number;
                        break;
                    case VnValueKind.Float:
                        view[pair.Key] = pair.Value.Number;
                        break;
                    case VnValueKind.Boolean:
                        view[pair.Key] = pair.Value.Bool;
                        break;
                    default:
                        view[pair.Key] = pair.Value.Text ?? string.Empty;
                        break;
                }
            }

            return view;
        }

        public VnSnapshot ExportSnapshot()
        {
            var variables = new Dictionary<string, VnValueData>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, VnValue> pair in _variables)
            {
                variables[pair.Key] = VnValueData.FromValue(pair.Value);
            }

            return new VnSnapshot
            {
                ProgramCounter = _programCounter,
                CallStack = new List<int>(_callStack),
                Variables = variables,
                Scene = new VnSceneStateData
                {
                    Background = _backgroundId,
                    Music = _musicTrackId,
                    Characters = _visibleCharacters.Select(character => new VnCharacterStateData
                    {
                        Id = character.Id,
                        Position = character.Position,
                        Expression = character.Expression
                    }).ToList()
                },
                History = _history.Select(ToData).ToList(),
                LastLine = CurrentLine == null ? null : ToData(CurrentLine),
                Finished = IsFinished
            };
        }

        public void ImportSnapshot(VnSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (snapshot.ProgramCounter < 0 || snapshot.ProgramCounter > _program.Instructions.Count)
            {
                throw new ArgumentException("存档中的程序计数器超出脚本范围。", nameof(snapshot));
            }

            Reset();
            _programCounter = snapshot.ProgramCounter;

            if (snapshot.Variables != null)
            {
                foreach (KeyValuePair<string, VnValueData> pair in snapshot.Variables)
                {
                    if (!string.IsNullOrEmpty(pair.Key) && pair.Value != null)
                    {
                        _variables[pair.Key] = pair.Value.ToValue();
                    }
                }
            }

            if (snapshot.CallStack != null)
            {
                foreach (int index in snapshot.CallStack)
                {
                    if (index < 0 || index > _program.Instructions.Count)
                    {
                        throw new ArgumentException("存档中的调用栈无效。", nameof(snapshot));
                    }

                    _callStack.Add(index);
                }
            }

            if (snapshot.History != null)
            {
                foreach (VnLineData entry in snapshot.History)
                {
                    if (entry != null)
                    {
                        _history.Add(VnHistoryEntry.FromData(entry));
                    }
                }
            }

            VnSceneStateData scene = snapshot.Scene ?? new VnSceneStateData();
            _backgroundId = scene.Background;
            _musicTrackId = scene.Music;

            if (scene.Characters != null)
            {
                foreach (VnCharacterStateData data in scene.Characters)
                {
                    if (data == null || string.IsNullOrEmpty(data.Id))
                    {
                        continue;
                    }

                    VnCharacter definition = _characters.GetOrFallback(data.Id);
                    _visibleCharacters.Add(new VnSceneCharacter(
                        definition.Id,
                        definition.DisplayName,
                        definition.Color,
                        string.IsNullOrEmpty(data.Position) ? "center" : data.Position,
                        string.IsNullOrEmpty(data.Expression) ? definition.DefaultExpression : data.Expression));
                }
            }

            CurrentLine = VnLine.FromData(snapshot.LastLine);
            IsFinished = snapshot.Finished;

            if (!IsFinished &&
                _programCounter >= 0 &&
                _programCounter < _program.Instructions.Count &&
                _program.Instructions[_programCounter] is ChoiceInstruction choice)
            {
                _currentChoices = BuildChoiceOptions(choice);
            }
        }

        private void Reset()
        {
            _programCounter = 0;
            _variables.Clear();
            _callStack.Clear();
            _history.Clear();
            _visibleCharacters.Clear();
            _currentChoices = Array.Empty<VnChoiceOption>();
            _backgroundId = null;
            _musicTrackId = null;
            CurrentLine = null;
            IsFinished = false;
        }

        private VnStepResult Execute()
        {
            int guard = 0;
            while (true)
            {
                if (++guard > MaxStepsPerAdvance)
                {
                    throw new VnRuntimeException("单次推进执行了过多指令，可能存在死循环跳转。");
                }

                if (IsFinished)
                {
                    return VnStepResult.ForEnd(CurrentLine);
                }

                if (_programCounter < 0 || _programCounter >= _program.Instructions.Count)
                {
                    IsFinished = true;
                    return VnStepResult.ForEnd(CurrentLine);
                }

                VnInstruction instruction = _program.Instructions[_programCounter];
                switch (instruction)
                {
                    case SayInstruction say:
                        CurrentLine = BuildLine(say);
                        _history.Add(VnHistoryEntry.FromLine(CurrentLine));
                        _programCounter++;
                        return VnStepResult.ForLine(CurrentLine);

                    case ChoiceInstruction choice:
                    {
                        _currentChoices = BuildChoiceOptions(choice);
                        return VnStepResult.ForChoice(CurrentLine, _currentChoices);
                    }

                    case BackgroundInstruction background:
                        _backgroundId = background.BackgroundId;
                        _programCounter++;
                        break;

                    case ShowCharacterInstruction show:
                        ShowCharacter(show);
                        _programCounter++;
                        break;

                    case HideCharacterInstruction hide:
                        _visibleCharacters.RemoveAll(character => character.Id == hide.CharacterId);
                        _programCounter++;
                        break;

                    case MusicInstruction music:
                        _musicTrackId = music.TrackId;
                        _programCounter++;
                        break;

                    case SetInstruction set:
                        ApplySet(set);
                        _programCounter++;
                        break;

                    case JumpInstruction jump:
                        _programCounter = ResolveLabel(jump.Label, jump.SourceLine);
                        break;

                    case CallInstruction call:
                        _callStack.Add(_programCounter + 1);
                        _programCounter = ResolveLabel(call.Label, call.SourceLine);
                        break;

                    case ReturnInstruction ret:
                        if (_callStack.Count == 0)
                        {
                            throw new VnRuntimeException("@return 时调用栈为空。", ret.SourceLine);
                        }

                        _programCounter = _callStack[_callStack.Count - 1];
                        _callStack.RemoveAt(_callStack.Count - 1);
                        break;

                    case ConditionalJumpInstruction conditional:
                        _programCounter = conditional.Condition.Evaluate(_variables)
                            ? _programCounter + 1
                            : conditional.TargetIndex;
                        break;

                    case JumpToInstruction jumpTo:
                        _programCounter = jumpTo.TargetIndex;
                        break;

                    case EndInstruction:
                        IsFinished = true;
                        return VnStepResult.ForEnd(CurrentLine);

                    default:
                        throw new VnRuntimeException($"未知指令类型 {instruction.GetType().Name}。", instruction.SourceLine);
                }
            }
        }

        private VnChoiceOption[] BuildChoiceOptions(ChoiceInstruction choice)
        {
            var options = new List<VnChoiceOption>();
            foreach (VnChoiceDefinition definition in choice.Options)
            {
                if (definition.Condition != null && !definition.Condition.Evaluate(_variables))
                {
                    continue;
                }

                options.Add(new VnChoiceOption(definition.Text, definition.TargetLabel));
            }

            if (options.Count == 0)
            {
                throw new VnRuntimeException("@choice 的所有选项条件都不满足，没有可选项。", choice.SourceLine);
            }

            return options.ToArray();
        }

        private VnLine BuildLine(SayInstruction say)
        {
            if (say.IsNarration)
            {
                return new VnLine(null, null, "#c8d2ff", say.Text);
            }

            VnCharacter character = _characters.GetOrFallback(say.SpeakerId);
            return new VnLine(character.Id, character.DisplayName, character.Color, say.Text);
        }

        private void ShowCharacter(ShowCharacterInstruction show)
        {
            VnCharacter character = _characters.GetOrFallback(show.CharacterId);
            string expression = string.IsNullOrWhiteSpace(show.Expression)
                ? character.DefaultExpression
                : show.Expression;

            var state = new VnSceneCharacter(
                character.Id,
                character.DisplayName,
                character.Color,
                show.Position,
                expression);

            int index = _visibleCharacters.FindIndex(existing => existing.Id == character.Id);
            if (index >= 0)
            {
                _visibleCharacters[index] = state;
            }
            else
            {
                _visibleCharacters.Add(state);
            }
        }

        private void ApplySet(SetInstruction set)
        {
            VnValue value = set.Value.Evaluate(_variables);
            if (set.Operator == VnSetOperator.Assign)
            {
                _variables[set.Variable] = value;
                return;
            }

            VnValue current = _variables.TryGetValue(set.Variable, out VnValue existing)
                ? existing
                : VnValue.Default;

            if (!current.TryGetNumber(out double left) || !value.TryGetNumber(out double right))
            {
                throw new VnRuntimeException($"变量「{set.Variable}」的 += / -= 需要数值。", set.SourceLine);
            }

            double result = set.Operator == VnSetOperator.Add ? left + right : left - right;
            bool bothInteger = current.Kind == VnValueKind.Integer && value.Kind == VnValueKind.Integer;
            _variables[set.Variable] = bothInteger
                ? VnValue.FromInteger((long)result)
                : VnValue.FromFloat(result);
        }

        private int ResolveLabel(string label, int sourceLine)
        {
            if (_program.TryGetLabel(label, out int index))
            {
                return index;
            }

            throw new VnRuntimeException($"标签「{label}」不存在。", sourceLine);
        }

        private static VnLineData ToData(VnLine line)
        {
            return new VnLineData
            {
                SpeakerId = line.SpeakerId,
                SpeakerName = line.SpeakerName,
                SpeakerColor = line.SpeakerColor,
                Text = line.Text,
                IsChoice = false
            };
        }

        private static VnLineData ToData(VnHistoryEntry entry)
        {
            return new VnLineData
            {
                SpeakerId = entry.SpeakerId,
                SpeakerName = entry.SpeakerName,
                SpeakerColor = entry.SpeakerColor,
                Text = entry.Text,
                IsChoice = entry.IsChoice
            };
        }
    }
}
