namespace CQ_ChartMaker.Models;

/// <summary>
/// 编辑器位置计算 —— 时间与屏幕 Y 坐标互转
/// </summary>
public static class PositionCalculator
{
    /// <summary>
    /// 目标时间（ms）→ 编辑器 Y 坐标（0=顶）
    /// </summary>
    public static double TimeToY(int targetTimeMs, int currentTimeMs,
        double pixelPerSecond, double judgmentLinePosition, double viewHeight)
    {
        double judgmentLineY = judgmentLinePosition * viewHeight;
        double deltaSec = (targetTimeMs - currentTimeMs) / 1000.0;
        return judgmentLineY - deltaSec * pixelPerSecond;
    }

    /// <summary>
    /// 编辑器 Y 坐标 → 时间（ms），反推用
    /// </summary>
    public static int YToTime(double y, int currentTimeMs,
        double pixelPerSecond, double judgmentLinePosition, double viewHeight)
    {
        double judgmentLineY = judgmentLinePosition * viewHeight;
        double deltaSec = (judgmentLineY - y) / pixelPerSecond;
        return currentTimeMs + (int)(deltaSec * 1000.0);
    }
}
