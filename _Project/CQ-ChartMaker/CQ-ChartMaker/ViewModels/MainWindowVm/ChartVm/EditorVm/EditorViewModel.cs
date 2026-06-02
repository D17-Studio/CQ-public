using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQ_ChartMaker.Models;
using CQ_ChartMaker.Models.Chart;

namespace CQ_ChartMaker.ViewModels;

public partial class EditorViewModel : ObservableObject
{
    public NotesViewModel NotesVM { get; }
    public LanesViewModel LanesVM { get; }
    public BeatsViewModel BeatsVM { get; }

    private readonly UserSettingsModel _userSettingsModel;

    [ObservableProperty]
    private object _currentSubView;

    [ObservableProperty]
    private bool _isNotesSelected = true;

    [ObservableProperty]
    private bool _isLanesSelected;

    [ObservableProperty]
    private bool _isBeatsSelected;

    partial void OnIsNotesSelectedChanged(bool value)
    {
        if (value) CurrentSubView = NotesVM;
    }

    partial void OnIsLanesSelectedChanged(bool value)
    {
        if (value) CurrentSubView = LanesVM;
    }

    partial void OnIsBeatsSelectedChanged(bool value)
    {
        if (value) CurrentSubView = BeatsVM;
    }

    public EditorViewModel(ChartSheetModel chartSheet, TimelineModel timeline, UserSettingsModel userSettingsModel)
    {
        _userSettingsModel = userSettingsModel;

        NotesVM = new NotesViewModel
        {
            ChartSheet   = chartSheet,
            Timeline     = timeline,
            UserSettings = userSettingsModel
        };
        LanesVM = new LanesViewModel
        {
            ChartSheet   = chartSheet,
            Timeline     = timeline,
            UserSettings = userSettingsModel
        };
        BeatsVM = new BeatsViewModel
        {
            ChartSheet   = chartSheet,
            Timeline     = timeline,
            UserSettings = userSettingsModel
        };

        CurrentSubView = NotesVM;
        _userSettingsModel.PropertyChanged += OnUserSettingModelChanged;
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

    #region 轨道显隐（7 个勾选框绑定，写入 UserSettings）

    public bool LaneNeg3Visible
    {
        get => _userSettingsModel.IsLaneVisible(-3);
        set { if (_userSettingsModel.IsLaneVisible(-3) == value) return; _userSettingsModel.SetLaneVisible(-3, value); OnPropertyChanged(); }
    }

    public bool LaneNeg2Visible
    {
        get => _userSettingsModel.IsLaneVisible(-2);
        set { if (_userSettingsModel.IsLaneVisible(-2) == value) return; _userSettingsModel.SetLaneVisible(-2, value); OnPropertyChanged(); }
    }

    public bool LaneNeg1Visible
    {
        get => _userSettingsModel.IsLaneVisible(-1);
        set { if (_userSettingsModel.IsLaneVisible(-1) == value) return; _userSettingsModel.SetLaneVisible(-1, value); OnPropertyChanged(); }
    }

    public bool Lane0Visible
    {
        get => _userSettingsModel.IsLaneVisible(0);
        set { if (_userSettingsModel.IsLaneVisible(0) == value) return; _userSettingsModel.SetLaneVisible(0, value); OnPropertyChanged(); }
    }

    public bool LanePos1Visible
    {
        get => _userSettingsModel.IsLaneVisible(1);
        set { if (_userSettingsModel.IsLaneVisible(1) == value) return; _userSettingsModel.SetLaneVisible(1, value); OnPropertyChanged(); }
    }

    public bool LanePos2Visible
    {
        get => _userSettingsModel.IsLaneVisible(2);
        set { if (_userSettingsModel.IsLaneVisible(2) == value) return; _userSettingsModel.SetLaneVisible(2, value); OnPropertyChanged(); }
    }

    public bool LanePos3Visible
    {
        get => _userSettingsModel.IsLaneVisible(3);
        set { if (_userSettingsModel.IsLaneVisible(3) == value) return; _userSettingsModel.SetLaneVisible(3, value); OnPropertyChanged(); }
    }

    private void OnUserSettingModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserSettingsModel.EditorZoom))
            OnPropertyChanged(nameof(ZoomText));
        else if (e.PropertyName == nameof(UserSettingsModel.Subdivision))
            OnPropertyChanged(nameof(Subdivision));
    }

    #endregion
}
