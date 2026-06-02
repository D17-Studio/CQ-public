using System.Collections.Generic;

namespace CQMusicGame.Shared
{
    /// <summary>
    /// 谱面Note数据集合，包装NoteData列表并提供统计查询
    /// </summary>
    public class ChartSheet
    {
        private readonly List<NoteData> _notes;
        private readonly List<LaneMotionEffect> _laneMotions;
        private readonly List<BpmPoint> _bpmPoints;

        /// <summary> Dot数量 </summary>
        public int DotCount { get; }

        /// <summary> Dash数量（不含附属Tune） </summary>
        public int DashCount { get; }

        /// <summary> Mute数量 </summary>
        public int MuteCount { get; }

        /// <summary> Tune数量（所有Dash的LinkedTunes之和） </summary>
        public int TuneCount { get; }

        /// <summary> 所有Note总数（Dot + Dash + Mute + Tune） </summary>
        public int TotalCount { get; }

        /// <summary>
        /// 传入谱面文本，内部调用ChartParser解析
        /// </summary>
        public ChartSheet(string chartText)
        {
            _notes = ChartParser.ParseNotes(chartText);
            _laneMotions = ChartParser.ParseLaneMotions(chartText);
            _bpmPoints = ChartParser.ParseBpmPoints(chartText);
            
            Sort();

            foreach (var note in _notes)
            {
                switch (note.Type)
                {
                    case NoteType.Dot:
                        DotCount++;
                        break;
                    case NoteType.Dash:
                        DashCount++;
                        if (note.LinkedTunes != null)
                            TuneCount += note.LinkedTunes.Length;
                        break;
                    case NoteType.Mute:
                        MuteCount++;
                        break;
                }
            }

            TotalCount = DotCount + DashCount + MuteCount + TuneCount;
        }

        /// <summary>
        /// 按时间顺序原地排序所有数据
        /// </summary>
        public void Sort()
        {
            _notes.Sort((a, b) => a.TargetTime.CompareTo(b.TargetTime));
            _laneMotions.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            _bpmPoints.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            foreach (var note in _notes)
            {
                if (note.LinkedTunes is { Length: > 1 })
                    System.Array.Sort(note.LinkedTunes, (a, b) => a.TargetTime.CompareTo(b.TargetTime));
            }
        }

        /// <summary>
        /// 排序并序列化为谱面文本（BPM → Note → LaneMotion）
        /// </summary>
        public string Serialize()
        {
            Sort();
            return ChartParser.SerializeBpmPoints(_bpmPoints)
                 + ChartParser.SerializeNotes(_notes)
                 + ChartParser.SerializeLaneMotions(_laneMotions);
        }

        /// <summary>
        /// 获取原始NoteData列表
        /// </summary>
        public List<NoteData> GetNotes() => _notes;
        public List<LaneMotionEffect> GetLaneMotions() => _laneMotions;
        public List<BpmPoint> GetBpmPoints() => _bpmPoints;
        public float GetBpmAt(int timeMs) => _bpmPoints.GetBpmAt(timeMs);
    }
}