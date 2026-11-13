using System.Collections.Generic;
using System.Diagnostics;
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

    /// <summary> 六类 LaneEvent 的编辑单元混合列表（Events 页按当前类型过滤使用） </summary>
    public List<ILaneEventUnit> LaneEventUnitList { get; }

    /// <summary> 各轨道"完全隐藏"（四通道不透明度全 0）的时间区间，随事件数据同步重建 </summary>
    public LaneHiddenRangeModel LaneHiddenRanges { get; } = new();

    public ChartSheetModel(string chartText)
    {
        // Parse 是严格解析：空白文本、旧 DSL、未知枚举、版本不符等都会抛 ChartException，
        // 由调用方（MainWindowViewModel）决定是保持当前谱面、还是新建一份合法谱面
        ChartSheet = ChartSheet.Parse(chartText, warning => Debug.WriteLine($"[谱面警告] {warning}"));

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
        BpmUnitList = ChartSheet.GetBpmList().Select(b => new BpmUnit { Data = b }).ToList();

        // 六类事件各自生成编辑单元；读档不是“新建事件”，直接使用 struct 数据，不走工厂
        LaneEventUnitList = new List<ILaneEventUnit>();
        foreach (var e in ChartSheet.GetTransformEvents())
            LaneEventUnitList.Add(new TransformEventUnit { Data = e });
        foreach (var e in ChartSheet.GetOpacityEvents())
            LaneEventUnitList.Add(new OpacityEventUnit { Data = e });
        foreach (var e in ChartSheet.GetLengthEvents())
            LaneEventUnitList.Add(new LengthEventUnit { Data = e });
        foreach (var e in ChartSheet.GetSpeedEvents())
            LaneEventUnitList.Add(new SpeedEventUnit { Data = e });
        foreach (var e in ChartSheet.GetZoneEvents())
            LaneEventUnitList.Add(new ZoneEventUnit { Data = e });
        foreach (var e in ChartSheet.GetParentEvents())
            LaneEventUnitList.Add(new ParentEventUnit { Data = e });

        // 谱面没有任何 BPM 点时补一个默认点（时间 0、BPM 200），保证节拍线与吸附始终可用
        // 同步回 struct 列表：节拍线生成、网格吸附、保存都以 struct 列表为准
        if (BpmUnitList.Count == 0)
        {
            BpmUnitList.Add(new BpmUnit { Data = new BpmData { StartTime = 0, Bpm = 200f } });
            SyncBpmsToData();
        }

        // 读档后立刻算一次隐藏区间，首次绘制即可显示变暗提示
        LaneHiddenRanges.Rebuild(this);
    }

    /// <summary>
    /// 当前数据实际使用的全部轨道编号（实时计算）：
    /// Note 的视觉轨、Tune 的目标轨、六类 LaneEvent 的轨道、Parent 事件引用的父轨。
    /// 用于轨道集合初始化、撤销后补轨、底栏“有数据/无数据”标记。
    /// </summary>
    public HashSet<int> GetDataLanes()
    {
        var lanes = new HashSet<int>();

        foreach (var note in NoteUnitList)
        {
            lanes.Add(note.Data.LaneIndex);
            if (note.TuneList == null) continue;
            foreach (var tune in note.TuneList)
                lanes.Add(tune.Data.TargetLane);
        }

        foreach (var e in ChartSheet.GetTransformEvents()) lanes.Add(e.LaneIndex);
        foreach (var e in ChartSheet.GetOpacityEvents()) lanes.Add(e.LaneIndex);
        foreach (var e in ChartSheet.GetLengthEvents()) lanes.Add(e.LaneIndex);
        foreach (var e in ChartSheet.GetSpeedEvents()) lanes.Add(e.LaneIndex);
        foreach (var e in ChartSheet.GetZoneEvents()) lanes.Add(e.LaneIndex);
        foreach (var e in ChartSheet.GetParentEvents())
        {
            lanes.Add(e.LaneIndex);
            if (e.ParentLane.HasValue) lanes.Add(e.ParentLane.Value);
        }

        return lanes;
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

    /// <summary>
    /// 把 LaneEventUnitList 的修改按具体类型同步回 ChartSheet 的六条 struct 列表，并统一排序。
    /// LaneState 求值默认列表按 StartTime 升序，重建前必须调用本方法。
    /// </summary>
    public void SyncEventsToData()
    {
        var transforms = ChartSheet.GetTransformEvents();
        var opacities  = ChartSheet.GetOpacityEvents();
        var lengths    = ChartSheet.GetLengthEvents();
        var speeds     = ChartSheet.GetSpeedEvents();
        var zones      = ChartSheet.GetZoneEvents();
        var parents    = ChartSheet.GetParentEvents();

        transforms.Clear();
        opacities.Clear();
        lengths.Clear();
        speeds.Clear();
        zones.Clear();
        parents.Clear();

        foreach (var unit in LaneEventUnitList)
        {
            switch (unit)
            {
                case TransformEventUnit u: transforms.Add(u.Data); break;
                case OpacityEventUnit u:   opacities.Add(u.Data);  break;
                case LengthEventUnit u:    lengths.Add(u.Data);    break;
                case SpeedEventUnit u:     speeds.Add(u.Data);     break;
                case ZoneEventUnit u:      zones.Add(u.Data);      break;
                case ParentEventUnit u:    parents.Add(u.Data);    break;
            }
        }

        ChartSheet.Sort();

        // 事件数据已变化，重建"完全隐藏"区间缓存（区间只随事件变化，与播放时间无关）
        LaneHiddenRanges.Rebuild(this);
    }

    /// <summary>
    /// 按当前 struct 列表现场构建 LaneState，用于新建事件/启用曲线时采样当前值。
    /// includeParents = false 时忽略父轨合成，返回轨道本地值（新曲线存的就是本地值）。
    /// 空状态下的默认值由 LaneState 的查询方法内置返回，调用方无需另写兜底。
    /// </summary>
    public LaneState CreateLaneState(bool includeParents)
    {
        var sheet = ChartSheet;
        IReadOnlyList<LaneEvent.Parent> parents = includeParents
            ? sheet.GetParentEvents()
            : System.Array.Empty<LaneEvent.Parent>();

        return new LaneState(
            sheet.GetTransformEvents(),
            sheet.GetOpacityEvents(),
            sheet.GetLengthEvents(),
            sheet.GetSpeedEvents(),
            sheet.GetZoneEvents(),
            parents);
    }

    /// <summary>
    /// 把 Note/BPM/LaneEvent 编辑列表同步回 struct 数据并序列化为 JSON 文本。
    /// </summary>
    public string Serialize()
    {
        SyncNotesToData();
        SyncBpmsToData();
        SyncEventsToData();
        ChartSheet.Sort(); // 写入前统一排序，保证文件内顺序稳定
        return ChartSerializer.Serialize(ChartSheet);
    }
}
