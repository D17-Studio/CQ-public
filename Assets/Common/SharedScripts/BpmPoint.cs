using System.Collections.Generic;

namespace CQMusicGame.Shared
{
    /// <summary>
    /// BPM变化点
    /// </summary>
    public struct BpmPoint
    {
        /// <summary> 开始时间（毫秒） </summary>
        public int StartTime;

        /// <summary> BPM值 </summary>
        public float Bpm;

        public BpmPoint(int startTime, float bpm)
        {
            StartTime = startTime;
            Bpm = bpm;
        }
    }

    /// <summary>
    /// BPM变化点列表的扩展查询
    /// </summary>
    public static class BpmPointExtensions
    {
        /// <summary>
        /// 查询指定时间的当前BPM（假设列表按StartTime升序排列）
        /// </summary>
        public static float GetBpmAt(this List<BpmPoint> points, int timeMs)
        {
            if (points == null || points.Count == 0)
                return 0;

            float bpm = points[0].Bpm;
            for (int i = 0; i < points.Count; i++)
            {
                if (timeMs >= points[i].StartTime)
                    bpm = points[i].Bpm;
                else
                    break;
            }
            return bpm;
        }
    }
}