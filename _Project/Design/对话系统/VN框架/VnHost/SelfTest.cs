using System.Text.Json;
using CQ.Vn;

namespace CQ.Vn.Host;

public static class SelfTest
{
    public static int Run(StoryBundle story)
    {
        int failures = 0;

        void Check(string name, bool passed)
        {
            Console.WriteLine((passed ? "[PASS] " : "[FAIL] ") + name);
            if (!passed)
            {
                failures++;
            }
        }

        VnMachine NewMachine() => new VnMachine(story.Program, story.Characters);

        Check("脚本解析成功", story.Program.Instructions.Count > 0);
        Check("存在 start 标签", story.Program.Labels.ContainsKey("start"));
        Check("角色表至少包含两名角色", story.Characters.Characters.Count >= 2);
        Check("脚本已声明标题", !string.IsNullOrWhiteSpace(story.Title));

        bool speakersValid = story.Program.Instructions
            .OfType<SayInstruction>()
            .Where(say => say.SpeakerId != null)
            .All(say => story.Characters.Characters.ContainsKey(say.SpeakerId));
        Check("台词 speakerId 都存在于角色表", speakersValid);

        bool showsValid = story.Program.Instructions
            .OfType<ShowCharacterInstruction>()
            .All(show => story.Characters.Characters.ContainsKey(show.CharacterId));
        Check("立绘角色 id 都存在于角色表", showsValid);

        RunResult good = RunStory(NewMachine(), _ => 0);
        Check("全选第一项可抵达结局", good.Ended);
        Check("全选第一项抵达好结局", good.LastLine?.Contains("黄昏之后", StringComparison.Ordinal) == true);
        Check("好结局路线出现两次选择", good.ChoiceCount == 2);
        Check("未询问时最终选择只有两个选项", good.OptionCounts.Count == 2 && good.OptionCounts[1] == 2);

        RunResult regret = RunStory(NewMachine(), _ => 1);
        Check("全选第二项可抵达结局", regret.Ended);
        Check("全选第二项抵达遗憾结局", regret.LastLine?.Contains("门外的听者", StringComparison.Ordinal) == true);

        RunResult normal = RunStory(NewMachine(), occurrence => occurrence == 0 ? 0 : 1);
        Check("加入后犹豫抵达普通结局", normal.LastLine?.Contains("明天见", StringComparison.Ordinal) == true);

        RunResult ask = RunStory(NewMachine(), _ => 2);
        Check("询问路线可抵达结局", ask.Ended);
        Check("询问后出现条件选项", ask.OptionCounts.Count >= 2 && ask.OptionCounts[1] == 3);
        Check("条件选项路线抵达好结局", ask.LastLine?.Contains("黄昏之后", StringComparison.Ordinal) == true);

        VnMachine atChoice = NewMachine();
        atChoice.Start();
        for (int guard = 0; guard < 200 && !atChoice.IsFinished && atChoice.CurrentChoices.Count == 0; guard++)
        {
            atChoice.Advance();
        }

        VnSnapshot choiceSnapshot = atChoice.ExportSnapshot();
        VnMachine restoredChoice = NewMachine();
        restoredChoice.ImportSnapshot(choiceSnapshot);
        Check("在读档时恢复等待中的选项",
            atChoice.CurrentChoices.Count > 0 &&
            restoredChoice.CurrentChoices.Count == atChoice.CurrentChoices.Count &&
            restoredChoice.CurrentChoices[0].Text == atChoice.CurrentChoices[0].Text);

        VnMachine original = NewMachine();
        original.Start();
        for (int step = 0; step < 6; step++)
        {
            if (original.CurrentChoices.Count > 0)
            {
                original.Choose(0);
            }
            else if (!original.IsFinished)
            {
                original.Advance();
            }
        }

        VnSnapshot snapshot = original.ExportSnapshot();
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        string json = JsonSerializer.Serialize(snapshot, jsonOptions);
        VnSnapshot? roundTrip = JsonSerializer.Deserialize<VnSnapshot>(json, jsonOptions);
        Check("存档 JSON 往返成功", roundTrip != null);

        VnMachine restored = NewMachine();
        if (roundTrip != null)
        {
            restored.ImportSnapshot(roundTrip);
        }

        Check("读档后当前行一致", restored.CurrentLine?.Text == original.CurrentLine?.Text);
        Check("读档后变量一致", VariablesEqual(original, restored));

        bool consistent = roundTrip != null;
        for (int step = 0; step < 16 && consistent; step++)
        {
            VnStepResult left = StepMachine(original);
            VnStepResult right = StepMachine(restored);
            consistent = StepsEqual(left, right);
            if (left.Kind == VnStepKind.Ended)
            {
                break;
            }
        }

        Check("读档后继续推进结果一致", consistent);

        Check("缺少 @endif 会报错", HasScriptError(":: start\n@if 好感 > 0\n旁白: 测试\n"));
        Check("未知跳转标签会报错", HasScriptError(":: start\n@jump nowhere\n"));
        Check("条件选项全部不可用会在运行时报错", HasRuntimeError(":: start\n@choice\n* 唯一选项 -> start [if 不存在]\n"));

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "全部自测通过。" : $"自测失败 {failures} 项。");
        return failures == 0 ? 0 : 1;
    }

    private static RunResult RunStory(VnMachine machine, Func<int, int> pickOption)
    {
        var optionCounts = new List<int>();
        string? lastLine = null;
        int choiceCount = 0;

        machine.Start();

        for (int guard = 0; guard < 2000 && !machine.IsFinished; guard++)
        {
            if (machine.CurrentChoices.Count > 0)
            {
                optionCounts.Add(machine.CurrentChoices.Count);
                int desired = pickOption(choiceCount++);
                int index = Math.Max(0, Math.Min(desired, machine.CurrentChoices.Count - 1));
                machine.Choose(index);
            }
            else
            {
                machine.Advance();
            }

            if (machine.CurrentLine != null)
            {
                lastLine = machine.CurrentLine.Text;
            }
        }

        if (machine.CurrentLine != null)
        {
            lastLine = machine.CurrentLine.Text;
        }

        return new RunResult(machine.IsFinished, lastLine, optionCounts, choiceCount);
    }

    private static VnStepResult StepMachine(VnMachine machine)
    {
        if (machine.IsFinished)
        {
            return VnStepResult.ForEnd(machine.CurrentLine);
        }

        return machine.CurrentChoices.Count > 0
            ? machine.Choose(0)
            : machine.Advance();
    }

    private static bool StepsEqual(VnStepResult left, VnStepResult right)
    {
        return left.Kind == right.Kind &&
               (left.Line?.Text ?? string.Empty) == (right.Line?.Text ?? string.Empty) &&
               left.Choices.Count == right.Choices.Count;
    }

    private static bool VariablesEqual(VnMachine left, VnMachine right)
    {
        Dictionary<string, object> leftView = left.GetVariableView();
        Dictionary<string, object> rightView = right.GetVariableView();

        if (leftView.Count != rightView.Count)
        {
            return false;
        }

        foreach (KeyValuePair<string, object> pair in leftView)
        {
            if (!rightView.TryGetValue(pair.Key, out object? value) || !Equals(pair.Value, value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasScriptError(string source)
    {
        try
        {
            VnScriptParser.Parse(source);
            return false;
        }
        catch (VnScriptException)
        {
            return true;
        }
    }

    private static bool HasRuntimeError(string source)
    {
        try
        {
            VnProgram program = VnScriptParser.Parse(source);
            var machine = new VnMachine(program, VnCharacterTable.Empty);
            machine.Start();
            return false;
        }
        catch (VnRuntimeException)
        {
            return true;
        }
    }

    private sealed record RunResult(
        bool Ended,
        string? LastLine,
        List<int> OptionCounts,
        int ChoiceCount);
}
