using System;
using System.Collections.Generic;
using System.Linq;

namespace CQMusicGame.Shared
{
    /// <summary>
    /// 谱面数据集合：Note、六类轨道事件与 BPM 列表，由模型集合构造并提供统计查询
    /// </summary>
    public class ChartSheet
    {
        private readonly List<NoteData> _noteList;
        private readonly List<LaneEvent.Transform> _transformEvents;
        private readonly List<LaneEvent.Opacity> _opacityEvents;
        private readonly List<LaneEvent.Length> _lengthEvents;
        private readonly List<LaneEvent.Speed> _speedEvents;
        private readonly List<LaneEvent.Zone> _zoneEvents;
        private readonly List<LaneEvent.Parent> _parentEvents;
        private readonly List<BpmData> _bpmList;
        private readonly List<int> _laneIndexList;

        /// <summary> Dot数量（不含Fake） </summary>
        public int DotCount { get; }

        /// <summary> Dash数量（不含Fake） </summary>
        public int DashCount { get; }

        /// <summary> Mute数量（不含Fake） </summary>
        public int MuteCount { get; }

        /// <summary> 所有Note总数（Dot + Dash + Mute，不含Fake） </summary>
        public int TotalCount { get; }

        /// <summary> 轨道运行时状态（构造时生成一次，全谱共用） </summary>
        public LaneState LaneState { get; }

        /// <summary>
        /// 解析谱面文本并构造谱面；解析细节由 ChartSerializer 负责
        /// </summary>
        /// <param name="text">JSON 谱面文本</param>
        /// <param name="onWarning">非致命问题回调，可为 null</param>
        public static ChartSheet Parse(string text, Action<string> onWarning = null)
            => ChartSerializer.Deserialize(text, onWarning);

        /// <summary>
        /// 创建空谱面（Note、六类轨道事件与 BPM 均为空集合）
        /// </summary>
        public ChartSheet()
            : this(Array.Empty<NoteData>(),
                   Array.Empty<LaneEvent.Transform>(),
                   Array.Empty<LaneEvent.Opacity>(),
                   Array.Empty<LaneEvent.Length>(),
                   Array.Empty<LaneEvent.Speed>(),
                   Array.Empty<LaneEvent.Zone>(),
                   Array.Empty<LaneEvent.Parent>(),
                   Array.Empty<BpmData>())
        {
        }

        /// <summary>
        /// 由模型集合构造谱面
        /// </summary>
        public ChartSheet(
            IReadOnlyList<NoteData> notes,
            IReadOnlyList<LaneEvent.Transform> transformEvents,
            IReadOnlyList<LaneEvent.Opacity> opacityEvents,
            IReadOnlyList<LaneEvent.Length> lengthEvents,
            IReadOnlyList<LaneEvent.Speed> speedEvents,
            IReadOnlyList<LaneEvent.Zone> zoneEvents,
            IReadOnlyList<LaneEvent.Parent> parentEvents,
            IReadOnlyList<BpmData> bpmList)
        {
            _noteList = new List<NoteData>(notes);
            _transformEvents = new List<LaneEvent.Transform>(transformEvents);
            _opacityEvents = new List<LaneEvent.Opacity>(opacityEvents);
            _lengthEvents = new List<LaneEvent.Length>(lengthEvents);
            _speedEvents = new List<LaneEvent.Speed>(speedEvents);
            _zoneEvents = new List<LaneEvent.Zone>(zoneEvents);
            _parentEvents = new List<LaneEvent.Parent>(parentEvents);
            _bpmList = new List<BpmData>(bpmList);

            Sort();

            LaneState = new LaneState(_transformEvents, _opacityEvents, _lengthEvents,
                                      _speedEvents, _zoneEvents, _parentEvents);
            InitializeNoteJudgeZones();
            _laneIndexList = BuildLaneIndexList();

            foreach (var note in _noteList)
            {
                if (note.Fake.HasValue)
                    continue;

                switch (note.Type)
                {
                    case NoteType.Dot:
                        DotCount++;
                        break;
                    case NoteType.Dash:
                        DashCount++;
                        break;
                    case NoteType.Mute:
                        MuteCount++;
                        break;
                }
            }

            TotalCount = DotCount + DashCount + MuteCount;
        }

        /// <summary>
        /// 按时间稳定排序所有列表（LaneEvent 同时间按 Duration 降序，其余同时间保持原有顺序）
        /// </summary>
        public void Sort()
        {
            StableSortBy(_noteList, note => note.TargetTime);
            StableSortBy(_transformEvents, e => e.StartTime, e => e.Duration);
            StableSortBy(_opacityEvents, e => e.StartTime, e => e.Duration);
            StableSortBy(_lengthEvents, e => e.StartTime, e => e.Duration);
            StableSortBy(_speedEvents, e => e.StartTime, e => e.Duration);
            StableSortBy(_zoneEvents, e => e.StartTime);
            StableSortBy(_parentEvents, e => e.StartTime);
            StableSortBy(_bpmList, bpm => bpm.StartTime);

            for (int i = 0; i < _noteList.Count; i++)
            {
                NoteData note = _noteList[i];
                if (note.LinkedTunes is not { Length: > 1 })
                    continue;

                note.LinkedTunes = note.LinkedTunes.OrderBy(tune => tune.TargetTime).ToArray();
                _noteList[i] = note;
            }
        }

        /// <summary> 获取Note列表 </summary>
        public List<NoteData> GetNoteList() => _noteList;

        /// <summary> 获取Transform事件列表 </summary>
        public List<LaneEvent.Transform> GetTransformEvents() => _transformEvents;

        /// <summary> 获取Opacity事件列表 </summary>
        public List<LaneEvent.Opacity> GetOpacityEvents() => _opacityEvents;

        /// <summary> 获取Length事件列表 </summary>
        public List<LaneEvent.Length> GetLengthEvents() => _lengthEvents;

        /// <summary> 获取Speed事件列表 </summary>
        public List<LaneEvent.Speed> GetSpeedEvents() => _speedEvents;

        /// <summary> 获取Zone事件列表 </summary>
        public List<LaneEvent.Zone> GetZoneEvents() => _zoneEvents;

        /// <summary> 获取Parent事件列表 </summary>
        public List<LaneEvent.Parent> GetParentEvents() => _parentEvents;

        /// <summary> 获取BPM列表 </summary>
        public List<BpmData> GetBpmList() => _bpmList;

        /// <summary> 谱面涉及的所有轨道序号（Note、Tune、六类事件、ParentLane），去重升序 </summary>
        public List<int> GetLaneIndexList() => _laneIndexList;

        /// <summary> 查询指定时间的BPM </summary>
        public float GetBpmAt(int timeMs) => _bpmList.GetBpmAt(timeMs);

        // NoteData.JudgeZone 为运行时派生值：按音符所在轨道在正解时刻的判定区固化
        private void InitializeNoteJudgeZones()
        {
            for (int i = 0; i < _noteList.Count; i++)
            {
                NoteData note = _noteList[i];
                _noteList[i] = note;
            }
        }

        private List<int> BuildLaneIndexList()
        {
            var laneIndices = new SortedSet<int>();

            foreach (var note in _noteList)
            {
                laneIndices.Add(note.LaneIndex);

                if (note.LinkedTunes == null) continue;

                foreach (var tune in note.LinkedTunes)
                    laneIndices.Add(tune.TargetLane);
            }

            foreach (var e in _transformEvents)
                laneIndices.Add(e.LaneIndex);
            foreach (var e in _opacityEvents)
                laneIndices.Add(e.LaneIndex);
            foreach (var e in _lengthEvents)
                laneIndices.Add(e.LaneIndex);
            foreach (var e in _speedEvents)
                laneIndices.Add(e.LaneIndex);
            foreach (var e in _zoneEvents)
                laneIndices.Add(e.LaneIndex);

            foreach (var e in _parentEvents)
            {
                laneIndices.Add(e.LaneIndex);

                if (e.ParentLane.HasValue)
                    laneIndices.Add(e.ParentLane.Value);
            }

            return new List<int>(laneIndices);
        }

        private static void StableSortBy<T>(List<T> list, Func<T, int> timeOf, Func<T, int> durationOf = null)
        {
            if (list.Count < 2)
                return;

            IOrderedEnumerable<T> ordered = list.OrderBy(timeOf);
            if (durationOf != null)
                ordered = ordered.ThenByDescending(durationOf);

            List<T> sorted = ordered.ToList();
            list.Clear();
            list.AddRange(sorted);
        }
    }
}
