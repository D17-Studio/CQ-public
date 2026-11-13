using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CQ.Vn
{
    public static class VnScriptParser
    {
        private static readonly Regex DialoguePattern = new Regex(
            @"^(?<speaker>[\p{L}_][\p{L}\p{N}_]*)\s*:\s*(?<text>.+)$",
            RegexOptions.Compiled);

        private static readonly Regex SetPattern = new Regex(
            @"^(?<var>\S+)\s*(?<op>\+=|-=|=)\s*(?<value>.+)$",
            RegexOptions.Compiled);

        private static readonly char[] WhitespaceSeparators = { ' ', '\t' };

        public static VnProgram Parse(string source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            string normalized = source.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            var instructions = new List<VnInstruction>();
            var labels = new Dictionary<string, int>(StringComparer.Ordinal);
            var ifStack = new Stack<IfFrame>();
            string title = null;

            int i = 0;
            while (i < lines.Length)
            {
                string line = lines[i].Trim();
                int lineNumber = i + 1;

                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    i++;
                    continue;
                }

                if (line.StartsWith("::", StringComparison.Ordinal))
                {
                    DefineLabel(labels, line.Substring(2).Trim(), instructions.Count, lineNumber);
                    i++;
                    continue;
                }

                if (line.StartsWith("@", StringComparison.Ordinal))
                {
                    ParsedCommand command = ParseCommand(line, lineNumber);
                    switch (command.Name)
                    {
                        case "title":
                            RequireArgument(command, lineNumber);
                            title = command.Argument;
                            i++;
                            break;

                        case "label":
                            DefineLabel(labels, RequireSingleToken(command, lineNumber, "标签名"), instructions.Count, lineNumber);
                            i++;
                            break;

                        case "bg":
                            RequireArgument(command, lineNumber);
                            instructions.Add(new BackgroundInstruction(lineNumber, command.Argument));
                            i++;
                            break;

                        case "bgm":
                            RequireArgument(command, lineNumber);
                            instructions.Add(new MusicInstruction(
                                lineNumber,
                                string.Equals(command.Argument, "stop", StringComparison.OrdinalIgnoreCase) ? null : command.Argument));
                            i++;
                            break;

                        case "show":
                            instructions.Add(ParseShow(command, lineNumber));
                            i++;
                            break;

                        case "hide":
                            instructions.Add(new HideCharacterInstruction(
                                lineNumber,
                                RequireSingleToken(command, lineNumber, "角色 id")));
                            i++;
                            break;

                        case "set":
                            instructions.Add(ParseSet(command, lineNumber));
                            i++;
                            break;

                        case "jump":
                            instructions.Add(new JumpInstruction(
                                lineNumber,
                                RequireSingleToken(command, lineNumber, "跳转标签")));
                            i++;
                            break;

                        case "call":
                            instructions.Add(new CallInstruction(
                                lineNumber,
                                RequireSingleToken(command, lineNumber, "调用标签")));
                            i++;
                            break;

                        case "return":
                            RequireNoArgument(command, lineNumber);
                            instructions.Add(new ReturnInstruction(lineNumber));
                            i++;
                            break;

                        case "end":
                            RequireNoArgument(command, lineNumber);
                            instructions.Add(new EndInstruction(lineNumber));
                            i++;
                            break;

                        case "if":
                        {
                            RequireArgument(command, lineNumber);
                            VnCondition condition = ParseCondition(command.Argument, lineNumber);
                            int falseJumpIndex = instructions.Count;
                            instructions.Add(new ConditionalJumpInstruction(lineNumber, condition));
                            ifStack.Push(new IfFrame(lineNumber, falseJumpIndex));
                            i++;
                            break;
                        }

                        case "elif":
                        {
                            RequireArgument(command, lineNumber);
                            if (ifStack.Count == 0)
                            {
                                throw new VnScriptException("@elif 没有对应的 @if。", lineNumber);
                            }

                            IfFrame frame = ifStack.Peek();
                            if (frame.HasElse)
                            {
                                throw new VnScriptException("@elif 不能出现在 @else 之后。", lineNumber);
                            }

                            instructions.Add(new JumpToInstruction(lineNumber));
                            frame.EndJumps.Add(instructions.Count - 1);
                            PatchFalseJump(instructions, frame, instructions.Count);
                            VnCondition condition = ParseCondition(command.Argument, lineNumber);
                            frame.FalseJumpIndex = instructions.Count;
                            instructions.Add(new ConditionalJumpInstruction(lineNumber, condition));
                            i++;
                            break;
                        }

                        case "else":
                        {
                            RequireNoArgument(command, lineNumber);
                            if (ifStack.Count == 0)
                            {
                                throw new VnScriptException("@else 没有对应的 @if。", lineNumber);
                            }

                            IfFrame frame = ifStack.Peek();
                            if (frame.HasElse)
                            {
                                throw new VnScriptException("同一个 @if 中出现了重复的 @else。", lineNumber);
                            }

                            instructions.Add(new JumpToInstruction(lineNumber));
                            frame.EndJumps.Add(instructions.Count - 1);
                            PatchFalseJump(instructions, frame, instructions.Count);
                            frame.HasElse = true;
                            i++;
                            break;
                        }

                        case "endif":
                        {
                            RequireNoArgument(command, lineNumber);
                            if (ifStack.Count == 0)
                            {
                                throw new VnScriptException("@endif 没有对应的 @if。", lineNumber);
                            }

                            IfFrame frame = ifStack.Pop();
                            PatchFalseJump(instructions, frame, instructions.Count);
                            foreach (int jumpIndex in frame.EndJumps)
                            {
                                ((JumpToInstruction)instructions[jumpIndex]).TargetIndex = instructions.Count;
                            }

                            i++;
                            break;
                        }

                        case "choice":
                        {
                            RequireNoArgument(command, lineNumber);
                            int choiceLine = lineNumber;
                            var options = new List<VnChoiceDefinition>();
                            i++;
                            while (i < lines.Length)
                            {
                                string candidate = lines[i].Trim();
                                if (candidate.Length == 0 || candidate.StartsWith("#", StringComparison.Ordinal))
                                {
                                    i++;
                                    continue;
                                }

                                if (!candidate.StartsWith("*", StringComparison.Ordinal))
                                {
                                    break;
                                }

                                options.Add(ParseChoiceOption(candidate, i + 1));
                                i++;
                            }

                            if (options.Count == 0)
                            {
                                throw new VnScriptException("@choice 后面至少要有一个以 * 开头的选项。", choiceLine);
                            }

                            instructions.Add(new ChoiceInstruction(choiceLine, options));
                            break;
                        }

                        default:
                            throw new VnScriptException($"未知指令 @{command.Name}。", lineNumber);
                    }

                    continue;
                }

                if (line.StartsWith("*", StringComparison.Ordinal))
                {
                    throw new VnScriptException("选项必须写在 @choice 之后。", lineNumber);
                }

                Match dialogue = DialoguePattern.Match(line);
                if (dialogue.Success)
                {
                    string text = dialogue.Groups["text"].Value.Trim();
                    if (text.Length == 0)
                    {
                        throw new VnScriptException("对话内容不能为空。", lineNumber);
                    }

                    instructions.Add(new SayInstruction(lineNumber, dialogue.Groups["speaker"].Value, text));
                }
                else
                {
                    instructions.Add(new SayInstruction(lineNumber, null, line));
                }

                i++;
            }

            if (ifStack.Count > 0)
            {
                throw new VnScriptException("@if 缺少对应的 @endif。", ifStack.Peek().SourceLine);
            }

            if (instructions.Count == 0)
            {
                throw new VnScriptException("脚本为空。");
            }

            ValidateTargets(instructions, labels);
            int entryIndex = labels.TryGetValue("start", out int startIndex) ? startIndex : 0;
            return new VnProgram(title, instructions, labels, entryIndex);
        }

        private static ParsedCommand ParseCommand(string line, int lineNumber)
        {
            string body = line.Substring(1).Trim();
            if (body.Length == 0)
            {
                throw new VnScriptException("指令名为空。", lineNumber);
            }

            int split = 0;
            while (split < body.Length && !char.IsWhiteSpace(body[split]))
            {
                split++;
            }

            string name = body.Substring(0, split).ToLowerInvariant();
            string argument = split < body.Length ? body.Substring(split).Trim() : string.Empty;
            return new ParsedCommand(name, argument);
        }

        private static void RequireArgument(ParsedCommand command, int lineNumber)
        {
            if (string.IsNullOrWhiteSpace(command.Argument))
            {
                throw new VnScriptException($"@{command.Name} 缺少参数。", lineNumber);
            }
        }

        private static void RequireNoArgument(ParsedCommand command, int lineNumber)
        {
            if (!string.IsNullOrWhiteSpace(command.Argument))
            {
                throw new VnScriptException($"@{command.Name} 不接受参数。", lineNumber);
            }
        }

        private static string RequireSingleToken(ParsedCommand command, int lineNumber, string description)
        {
            RequireArgument(command, lineNumber);
            if (command.Argument.IndexOfAny(WhitespaceSeparators) >= 0)
            {
                throw new VnScriptException($"@{command.Name} 的{description}不能包含空白字符。", lineNumber);
            }

            return command.Argument;
        }

        private static VnInstruction ParseShow(ParsedCommand command, int lineNumber)
        {
            RequireArgument(command, lineNumber);
            string[] tokens = command.Argument.Split(WhitespaceSeparators, StringSplitOptions.RemoveEmptyEntries);
            string characterId = tokens[0];
            string position = "center";
            string expression = null;

            if (tokens.Length >= 2)
            {
                if (IsPosition(tokens[1]))
                {
                    position = tokens[1].ToLowerInvariant();
                    if (tokens.Length >= 3)
                    {
                        expression = string.Join(" ", tokens.Skip(2));
                    }
                }
                else
                {
                    expression = string.Join(" ", tokens.Skip(1));
                }
            }

            return new ShowCharacterInstruction(lineNumber, characterId, position, expression);
        }

        private static bool IsPosition(string token)
        {
            return string.Equals(token, "left", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(token, "center", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(token, "right", StringComparison.OrdinalIgnoreCase);
        }

        private static VnInstruction ParseSet(ParsedCommand command, int lineNumber)
        {
            RequireArgument(command, lineNumber);
            Match match = SetPattern.Match(command.Argument);
            if (!match.Success)
            {
                throw new VnScriptException("@set 语法应为：@set 变量 = 值，或使用 += / -=。", lineNumber);
            }

            string operatorText = match.Groups["op"].Value;
            VnSetOperator setOperator = operatorText == "+="
                ? VnSetOperator.Add
                : operatorText == "-="
                    ? VnSetOperator.Subtract
                    : VnSetOperator.Assign;

            if (!VnOperand.TryParse(match.Groups["value"].Value, out VnOperand operand, out string error))
            {
                throw new VnScriptException("@set 值无效：" + error, lineNumber);
            }

            return new SetInstruction(lineNumber, match.Groups["var"].Value, setOperator, operand);
        }

        private static VnChoiceDefinition ParseChoiceOption(string line, int lineNumber)
        {
            string body = line.Substring(1).Trim();
            int arrow = body.LastIndexOf("->", StringComparison.Ordinal);
            if (arrow <= 0)
            {
                throw new VnScriptException("选项语法应为：* 文本 -> 标签 [if 条件]。", lineNumber);
            }

            string text = body.Substring(0, arrow).Trim();
            if (text.Length == 0)
            {
                throw new VnScriptException("选项文本不能为空。", lineNumber);
            }

            string targetText = body.Substring(arrow + 2).Trim();
            string targetLabel;
            VnCondition condition = null;

            int bracket = targetText.IndexOf('[');
            if (bracket >= 0)
            {
                targetLabel = targetText.Substring(0, bracket).Trim();
                string conditionTail = targetText.Substring(bracket).Trim();
                if (!conditionTail.StartsWith("[if", StringComparison.OrdinalIgnoreCase) ||
                    !conditionTail.EndsWith("]", StringComparison.Ordinal))
                {
                    throw new VnScriptException("选项条件语法应为：[if 条件]。", lineNumber);
                }

                string conditionText = conditionTail.Substring(3, conditionTail.Length - 4).Trim();
                if (conditionText.Length == 0)
                {
                    throw new VnScriptException("选项条件不能为空。", lineNumber);
                }

                condition = ParseCondition(conditionText, lineNumber);
            }
            else
            {
                targetLabel = targetText;
            }

            if (targetLabel.Length == 0 || targetLabel.IndexOfAny(WhitespaceSeparators) >= 0)
            {
                throw new VnScriptException("选项目标标签无效。", lineNumber);
            }

            return new VnChoiceDefinition(text, targetLabel, condition);
        }

        private static VnCondition ParseCondition(string text, int lineNumber)
        {
            if (!VnCondition.TryParse(text, out VnCondition condition, out string error))
            {
                throw new VnScriptException("条件无效：" + error, lineNumber);
            }

            return condition;
        }

        private static void DefineLabel(Dictionary<string, int> labels, string label, int index, int lineNumber)
        {
            if (label.Length == 0)
            {
                throw new VnScriptException("标签名不能为空。", lineNumber);
            }

            if (label.IndexOfAny(WhitespaceSeparators) >= 0)
            {
                throw new VnScriptException("标签名不能包含空白字符。", lineNumber);
            }

            if (labels.ContainsKey(label))
            {
                throw new VnScriptException($"标签「{label}」重复定义。", lineNumber);
            }

            labels[label] = index;
        }

        private static void PatchFalseJump(List<VnInstruction> instructions, IfFrame frame, int targetIndex)
        {
            if (frame.FalseJumpIndex < 0)
            {
                return;
            }

            ((ConditionalJumpInstruction)instructions[frame.FalseJumpIndex]).TargetIndex = targetIndex;
            frame.FalseJumpIndex = -1;
        }

        private static void ValidateTargets(List<VnInstruction> instructions, Dictionary<string, int> labels)
        {
            foreach (VnInstruction instruction in instructions)
            {
                switch (instruction)
                {
                    case JumpInstruction jump when !labels.ContainsKey(jump.Label):
                        throw new VnScriptException($"跳转标签「{jump.Label}」不存在。", jump.SourceLine);

                    case CallInstruction call when !labels.ContainsKey(call.Label):
                        throw new VnScriptException($"调用标签「{call.Label}」不存在。", call.SourceLine);

                    case ChoiceInstruction choice:
                        foreach (VnChoiceDefinition option in choice.Options)
                        {
                            if (!labels.ContainsKey(option.TargetLabel))
                            {
                                throw new VnScriptException($"选项目标标签「{option.TargetLabel}」不存在。", choice.SourceLine);
                            }
                        }

                        break;

                    case ConditionalJumpInstruction conditional when conditional.TargetIndex < 0:
                        throw new VnScriptException("内部条件跳转目标未闭合。", conditional.SourceLine);

                    case JumpToInstruction jumpTo when jumpTo.TargetIndex < 0:
                        throw new VnScriptException("内部跳转目标未闭合。", jumpTo.SourceLine);
                }
            }
        }

        private sealed class ParsedCommand
        {
            public ParsedCommand(string name, string argument)
            {
                Name = name;
                Argument = argument;
            }

            public string Name { get; }
            public string Argument { get; }
        }

        private sealed class IfFrame
        {
            public IfFrame(int sourceLine, int falseJumpIndex)
            {
                SourceLine = sourceLine;
                FalseJumpIndex = falseJumpIndex;
            }

            public int SourceLine { get; }
            public int FalseJumpIndex { get; set; }
            public bool HasElse { get; set; }
            public List<int> EndJumps { get; } = new List<int>();
        }
    }
}
