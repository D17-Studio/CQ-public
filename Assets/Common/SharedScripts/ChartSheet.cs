using System.Collections.Generic;

namespace CQMusicGame.Shared
{
    /// <summary>
    /// 谱面Note数据集合，包装NoteData列表并提供统计查询
    /// </summary>
    public class ChartSheet
    {
        private readonly List<NoteData> _noteList;
        private readonly List<LaneMotionData> _laneMotionList;
        private readonly List<BpmData> _bpmList;

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
            _noteList = ChartParser.ParseNotes(chartText);
            _laneMotionList = ChartParser.ParseLaneMotions(chartText);
            _bpmList = ChartParser.ParseBpmPoints(chartText);
            
            Sort();

            foreach (var note in _noteList)
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
            _noteList.Sort((a, b) => a.TargetTime.CompareTo(b.TargetTime));
            _laneMotionList.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            _bpmList.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            foreach (var note in _noteList)
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
            return ChartParser.SerializeBpmPoints(_bpmList)
                 + ChartParser.SerializeNotes(_noteList)
                 + ChartParser.SerializeLaneMotions(_laneMotionList);
        }

        /// <summary>
        /// 获取原始NoteData列表
        /// </summary>
        public List<NoteData> GetNoteList() => _noteList;
        public List<LaneMotionData> GetLaneMotionList() => _laneMotionList;
        public List<BpmData> GetBpmList() => _bpmList;
        
        
        public float GetBpmAt(int timeMs) => _bpmList.GetBpmAt(timeMs);
    }
}