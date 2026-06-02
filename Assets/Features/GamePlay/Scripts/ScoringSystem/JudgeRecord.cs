using CQMusicGame.Shared;

/// <summary>
/// Claude: 判定记录结构体，存储判定结果和偏移量
/// </summary>
public struct JudgeRecord
{
    /// <summary>
    /// 判定结果
    /// </summary>
    public JudgeResult Result;

    /// <summary>
    /// 偏移量（毫秒），Mute固定为0
    /// </summary>
    public int Offset;

    public JudgeRecord(JudgeResult result, int offset = 0)
    {
        Result = result;
        Offset = offset;
    }
}