using System.Collections.Generic;
using System.Linq;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.Models;

/// <summary>
/// 谱面数据模型，持有 struct 数据源（ChartSheet）和 class 编辑列表（Unit）
/// </summary>
public class ChartSheetModel
{
    public ChartSheet ChartSheet { get; }

    public List<NoteUnit> NoteUnitList { get; }
    public List<BpmUnit> BpmUnitList { get; }
    public List<MotionUnit> MotionUnitList { get; }

    public ChartSheetModel(string chartText)
    {
        ChartSheet = new ChartSheet(chartText);
        NoteUnitList = new List<NoteUnit>();
        foreach (var noteData in ChartSheet.GetNoteList())
        {
            var unit = new NoteUnit { Data = noteData };
            if (noteData.LinkedTunes != null)
            {
                unit.TuneList = noteData.LinkedTunes
                    .Select(t => new TuneUnit { Parent = unit, Data = t })
                    .ToList();
                unit.Data.LinkedTunes = null;
            }
            NoteUnitList.Add(unit);
        }
        BpmUnitList    = ChartSheet.GetBpmList().Select(b => new BpmUnit { Data = b }).ToList();
        MotionUnitList = ChartSheet.GetLaneMotionList().Select(m => new MotionUnit { Data = m }).ToList();

        // 谱面没有任何 BPM 点时补一个默认点（时间 0、BPM 200），保证节拍线与吸附始终可用
        // 同步回 struct 列表：节拍线生成、网格吸附、保存都以 struct 列表为准
        if (BpmUnitList.Count == 0)
        {
            BpmUnitList.Add(new BpmUnit { Data = new BpmData(0, 200f) });
            SyncBpmsToData();
        }
    }

    /// <summary> 将 NoteUnitList 的修改同步回 ChartSheet 的 NoteData List 并排序 </summary>
    public void SyncNotesToData()
    {
        foreach (var unit in NoteUnitList)
        {
            if (unit.TuneList != null)
                unit.Data.LinkedTunes = unit.TuneList.Select(t => t.Data).ToArray();
        }

        var list = ChartSheet.GetNoteList();
        list.Clear();
        list.AddRange(NoteUnitList.Select(u => u.Data));
        list.Sort((a, b) => a.TargetTime.CompareTo(b.TargetTime));
        foreach (var note in list)
        {
            if (note.LinkedTunes is { Length: > 1 })
                System.Array.Sort(note.LinkedTunes, (a, b) => a.TargetTime.CompareTo(b.TargetTime));
        }
    }

    /// <summary> 将 BpmUnitList 的修改同步回 ChartSheet 的 BpmData List 并排序 </summary>
    public void SyncBpmsToData()
    {
        var list = ChartSheet.GetBpmList();
        list.Clear();
        list.AddRange(BpmUnitList.Select(u => u.Data));
        list.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
    }

    /// <summary> 将 MotionUnitList 的修改同步回 ChartSheet 的 LaneMotionData List 并排序 </summary>
    public void SyncMotionsToData()
    {
        var list = ChartSheet.GetLaneMotionList();
        list.Clear();
        list.AddRange(MotionUnitList.Select(u => u.Data));
        list.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
    }
}
