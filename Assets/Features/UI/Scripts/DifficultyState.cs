/// <summary>
/// 难度栏一条难度的显示数据（Presenter → View 的下推契约）：有无谱面 / 定数 / 是否选中。
/// </summary>
public readonly struct DifficultyState
{
    public readonly ChartDif Difficulty;
    public readonly bool HasChart;
    public readonly int Constant;
    public readonly bool Selected;

    public DifficultyState(ChartDif difficulty, bool hasChart, int constant, bool selected)
    {
        Difficulty = difficulty;
        HasChart = hasChart;
        Constant = constant;
        Selected = selected;
    }
}
