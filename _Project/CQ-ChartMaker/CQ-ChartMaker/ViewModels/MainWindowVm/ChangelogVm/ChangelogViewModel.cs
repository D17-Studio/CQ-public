using CQ_ChartMaker.Common;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CQ_ChartMaker.ViewModels;

/// <summary> 更新日志窗口的 ViewModel </summary>
public partial class ChangelogViewModel : ObservableObject
{
    /// <summary> 更新日志文本（来源 AppInfo.Changelog，版本从新到旧） </summary>
    public string Changelog => AppInfo.Changelog;
}
