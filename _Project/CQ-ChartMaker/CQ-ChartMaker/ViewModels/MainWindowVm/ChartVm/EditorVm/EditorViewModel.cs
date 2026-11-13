using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQ_ChartMaker.Models;

namespace CQ_ChartMaker.ViewModels;

public partial class EditorViewModel : ObservableObject
{
    public NotesViewModel NotesVM { get; }
    public EventsViewModel EventsVm { get; }
    public BeatsViewModel BeatsVM { get; }

    private readonly UserSettingsModel _userSettingsModel;
    private readonly ChartSheetModel _chartSheet;
    private readonly LaneCatalogModel _laneCatalog;

    /// <summary> 点击选中回调（unit, 是否 Ctrl），由 ChartVM 注入，EditorVM 转发给 NotesVM </summary>
    public System.Action<object?, bool>? OnSelectUnit
    {
        get => NotesVM.OnSelectUnit;
        set => NotesVM.OnSelectUnit = value;
    }

    /// <summary> 切换子视图回调，由 ChartVM 注入（用于取消幽灵/重定位等切换清理） </summary>
    public System.Action? OnSubViewSwitched;

    /// <summary> 切换 Events 事件类型回调，由 ChartVM 注入（统一处理幽灵/重定位/选中的清理） </summary>
    public System.Action<LaneEventKind>? OnSwitchLaneEventKind;

    [ObservableProperty]
    private object _currentSubView;

    [ObservableProperty]
    private bool _isNotesSelected = true;

    [ObservableProperty]
    private bool _isEventsSelected;

    [ObservableProperty]
    private bool _isBeatsSelected;

    partial void OnIsNotesSelectedChanged(bool value)
    {
        if (!value) return;
        SetActiveSubView(NotesVM);
    }

    partial void OnIsEventsSelectedChanged(bool value)
    {
        if (!value) return;
        SetActiveSubView(EventsVm);
    }

    partial void OnIsBeatsSelectedChanged(bool value)
    {
        if (!value) return;
        SetActiveSubView(BeatsVM);
    }

    /// <summary>
    /// 切换当前子视图：先换 ContentControl 内容，再更新三个 VM 的活动标志。
    /// 只有活动视图会响应时间变化刷新；新激活的 VM 会在 IsActive 置位时补一次刷新
    /// </summary>
    private void SetActiveSubView(object vm)
    {
        CurrentSubView = vm;
        NotesVM.IsActive = ReferenceEquals(vm, NotesVM);
        EventsVm.IsActive = ReferenceEquals(vm, EventsVm);
        BeatsVM.IsActive = ReferenceEquals(vm, BeatsVM);
        OnSubViewSwitched?.Invoke();
    }

    public EditorViewModel(ChartSheetModel chartSheet, TimelineModel timeline,
        UserSettingsModel userSettingsModel, LaneCatalogModel laneCatalog)
    {
        _userSettingsModel = userSettingsModel;
        _chartSheet = chartSheet;
        _laneCatalog = laneCatalog;

        NotesVM = new NotesViewModel
        {
            ChartSheet   = chartSheet,
            Timeline     = timeline,
            UserSettings = userSettingsModel,
            LaneCatalog  = laneCatalog
        };
        EventsVm = new EventsViewModel
        {
            ChartSheet   = chartSheet,
            Timeline     = timeline,
            UserSettings = userSettingsModel,
            LaneCatalog  = laneCatalog
        };
        // 底栏语义随显示模式变化：轨道/平铺=显隐，类型=当前轨道；模式变化时重建底栏
        EventsVm.PropertyChanged += OnEventsVmPropertyChanged;
        // Beats 是单轨 BPM 面板，不参与多轨显隐
        BeatsVM = new BeatsViewModel
        {
            ChartSheet   = chartSheet,
            Timeline     = timeline,
            UserSettings = userSettingsModel
        };

        // 拖拽默认开启，用户可在设置-偏好中禁用（持久化到 UserSettings.IsDragDisabled）
        ApplyDragSetting();

        SetActiveSubView(NotesVM);
        _userSettingsModel.PropertyChanged += OnUserSettingModelChanged;

        // 轨道集合增删后重建底栏；显隐切换由 LaneBarItem 自身写入 catalog，无需重建
        _laneCatalog.Changed += RefreshLaneBar;
        RefreshLaneBar();

        // 平铺模式类型显隐变化后同步类型栏按钮状态
        EventsVm.KindVisibilityChanged += RefreshTypeBar;
        RefreshTypeBar();
    }

    #region 缩放控制

    public string ZoomText => $"{_userSettingsModel.EditorZoom}%";

    [RelayCommand]
    private void ZoomIn()
    {
        int z = _userSettingsModel.EditorZoom + 10;
        if (z > 120) z = 120;
        _userSettingsModel.EditorZoom = z;
    }

    [RelayCommand]
    private void ZoomOut()
    {
        int z = _userSettingsModel.EditorZoom - 10;
        if (z < 20) z = 20;
        _userSettingsModel.EditorZoom = z;
    }

    #endregion

    #region 细分控制

    public Subdivision Subdivision
    {
        get => _userSettingsModel.Subdivision;
        set
        {
            if (_userSettingsModel.Subdivision == value) return;
            _userSettingsModel.Subdivision = value;
            OnPropertyChanged();
        }
    }

    #endregion

    #region 事件类型栏

    /// <summary>
    /// 类型栏按钮集合。三种模式共用同一控件：
    /// 轨道=单选当前类型；平铺=多选类型显隐；类型=全部禁用且未选中。
    /// </summary>
    [ObservableProperty]
    private List<TypeBarItem> _typeBarItems = new();

    /// <summary> 重建类型栏按钮状态（模式/当前类型/类型显隐变化后调用） </summary>
    private void RefreshTypeBar()
    {
        bool byLane = EventsVm.DisplayMode == LaneDisplayMode.ByLane;
        bool free = EventsVm.DisplayMode == LaneDisplayMode.Free;

        var items = new List<TypeBarItem>(LaneEventKindInfo.Count);
        for (int i = 0; i < LaneEventKindInfo.Count; i++)
        {
            var kind = (LaneEventKind)i;
            bool isChecked = free
                ? EventsVm.IsKindVisible(kind)
                : !byLane && kind == EventsVm.CurrentEventKind;

            items.Add(new TypeBarItem(kind, LaneEventKindInfo.GetLabel(kind),
                isChecked, !byLane, OnTypeBarItemChecked));
        }
        TypeBarItems = items;
    }

    /// <summary> 类型按钮点击：轨道=单选切换；平铺=切换类型显隐；类型=忽略 </summary>
    private void OnTypeBarItemChecked(LaneEventKind kind, bool isChecked)
    {
        if (EventsVm.DisplayMode == LaneDisplayMode.ByLane) return;

        if (EventsVm.DisplayMode == LaneDisplayMode.Free)
        {
            EventsVm.SetKindVisible(kind, isChecked);
            return;
        }

        if (isChecked)
        {
            if (EventsVm.CurrentEventKind != kind)
                OnSwitchLaneEventKind?.Invoke(kind);
        }
        else if (EventsVm.CurrentEventKind == kind)
        {
            // 单击已选中的类型会取消勾选：重建按钮恢复“必有一个选中”
            RefreshTypeBar();
        }
    }

    #endregion

    #region 显示模式

    /// <summary> 模式下拉框的 SelectedIndex（0=轨道，1=类型，2=平铺） </summary>
    public int DisplayModeIndex
    {
        get => (int)EventsVm.DisplayMode;
        set
        {
            if (value < 0 || value > (int)LaneDisplayMode.Free) return;
            if ((int)EventsVm.DisplayMode == value) return;
            EventsVm.DisplayMode = (LaneDisplayMode)value;
            OnPropertyChanged();
        }
    }

    private void OnEventsVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var name = e.PropertyName;
        if (name == nameof(EventsViewModel.CurrentLane) ||
            name == nameof(EventsViewModel.DisplayMode))
        {
            // 显示模式写回用户设置并自动持久化；恢复时值相同，加判断避免重复落盘
            if (name == nameof(EventsViewModel.DisplayMode) &&
                _userSettingsModel.EventsDisplayMode != EventsVm.DisplayMode)
            {
                _userSettingsModel.EventsDisplayMode = EventsVm.DisplayMode;
            }

            RefreshLaneBar();
            RefreshTypeBar();
            OnPropertyChanged(nameof(DisplayModeIndex));
        }
        else if (name == nameof(EventsViewModel.CurrentEventKind))
        {
            RefreshTypeBar();
        }
    }

    #endregion

    #region 轨道栏

    /// <summary> 底栏轨道项（轨道按钮 + 跳号/尾部新增按钮） </summary>
    [ObservableProperty]
    private List<LaneBarItem> _laneBarItems = new();

    /// <summary>
    /// 重建底栏项：轨道集合或“是否有数据”变化后调用。
    /// 跳号 + 只出现在非负区间的空隙处；尾部 + 创建最大标号 + 1。
    /// </summary>
    public void RefreshLaneBar()
    {
        var dataLanes = _chartSheet.GetDataLanes();
        var lanes = _laneCatalog.Lanes;
        var items = new List<LaneBarItem>(lanes.Count + 2);

        // 类型模式：底栏按钮语义变为“选择当前轨道”，IsVisible 只作为亮/暗镜像
        bool byLane = EventsVm.DisplayMode == LaneDisplayMode.ByLane;

        for (int i = 0; i < lanes.Count; i++)
        {
            int lane = lanes[i];

            // 跳号新增：相邻轨道差 > 1 时，在两者之间插入“补最小跳号”的 +。
            // 负标号没有创建途径，所以只在非负区间显示
            if (i > 0)
            {
                int prev = lanes[i - 1];
                if (lane - prev > 1 && prev + 1 >= 0)
                {
                    items.Add(new LaneBarItem(LaneBarItemKind.GapAdd, prev + 1,
                        onAdd: AddLaneAt));
                }
            }

            items.Add(new LaneBarItem(LaneBarItemKind.Lane, lane,
                isVisible: byLane ? lane == EventsVm.CurrentLane : _laneCatalog.IsVisible(lane),
                isDefault: LaneCatalogModel.IsDefaultLane(lane),
                hasData: dataLanes.Contains(lane),
                onVisibilityChanged: byLane ? SetCurrentLane : SetLaneVisible,
                onDelete: DeleteLane));
        }

        // 尾部新增：始终创建当前最大标号 + 1
        if (lanes.Count > 0)
        {
            items.Add(new LaneBarItem(LaneBarItemKind.TailAdd, lanes[^1] + 1,
                onAdd: AddLaneAt));
        }

        LaneBarItems = items;
    }

    /// <summary> 底栏切换显隐：写回会话内轨道集合 </summary>
    private void SetLaneVisible(int lane, bool visible) => _laneCatalog.SetVisible(lane, visible);

    /// <summary> 类型模式：底栏按钮切换当前轨道 </summary>
    private void SetCurrentLane(int lane, bool selected)
    {
        if (selected)
        {
            EventsVm.CurrentLane = lane;
            return;
        }

        // 点击已选中的按钮会把它取消勾选：重建底栏，保证始终有一条亮轨道
        if (EventsVm.CurrentLane == lane) RefreshLaneBar();
    }

    /// <summary> 点击 +：创建目标编号的轨道（已存在则无操作） </summary>
    private void AddLaneAt(LaneBarItem item)
    {
        _laneCatalog.Add(item.LaneIndex);
        // 类型模式：新增后直接切换到新轨道
        if (EventsVm.DisplayMode == LaneDisplayMode.ByLane)
            EventsVm.CurrentLane = item.LaneIndex;
    }

    /// <summary> 右键删除：只允许删除非默认且无数据的空轨道 </summary>
    private void DeleteLane(LaneBarItem item)
    {
        if (!item.CanDelete) return;

        bool wasCurrent = EventsVm.DisplayMode == LaneDisplayMode.ByLane &&
                          item.LaneIndex == EventsVm.CurrentLane;
        int deleted = item.LaneIndex;
        if (!_laneCatalog.Remove(deleted)) return;
        if (!wasCurrent) return;

        // 类型模式删除当前轨道：回退到更低一级标号的轨道；没有则取最小轨道
        var lanes = _laneCatalog.Lanes;
        int fallback = lanes.Count > 0 ? lanes[0] : 0;
        for (int i = lanes.Count - 1; i >= 0; i--)
        {
            if (lanes[i] < deleted) { fallback = lanes[i]; break; }
        }
        EventsVm.CurrentLane = fallback;
    }

    private void OnUserSettingModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserSettingsModel.EditorZoom))
            OnPropertyChanged(nameof(ZoomText));
        else if (e.PropertyName == nameof(UserSettingsModel.Subdivision))
            OnPropertyChanged(nameof(Subdivision));
        else if (e.PropertyName == nameof(UserSettingsModel.IsDragDisabled))
            ApplyDragSetting();
    }

    /// <summary>
    /// 把用户设置中的“禁用拖拽”下发到三个子视图。
    /// 禁用时子 VM 会各自取消正在进行的拖拽并还原原对象。
    /// </summary>
    private void ApplyDragSetting()
    {
        bool enabled = !_userSettingsModel.IsDragDisabled;
        NotesVM.IsDragMode = enabled;
        EventsVm.IsDragMode = enabled;
        BeatsVM.IsDragMode = enabled;
    }

    #endregion
}
