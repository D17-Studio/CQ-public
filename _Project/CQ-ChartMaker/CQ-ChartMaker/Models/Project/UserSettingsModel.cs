using CommunityToolkit.Mvvm.ComponentModel;

namespace CQ_ChartMaker.Models;

public partial class UserSettingsModel : ObservableObject
{
    private bool _suppressSave;

    [ObservableProperty]
    private double _pixelPerSecond = 200.0;

    partial void OnPixelPerSecondChanged(double value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 轨道显隐位掩码（bit 0~6 对应 lane -3~3，1=可见，默认全开 0x7F） </summary>
    [ObservableProperty]
    private int _laneVisibilityMask = 0x7F;

    partial void OnLaneVisibilityMaskChanged(int value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 查询指定 lane（-3~3）是否可见 </summary>
    public bool IsLaneVisible(int lane) => (LaneVisibilityMask & (1 << (lane + 3))) != 0;

    /// <summary> 设置指定 lane（-3~3）是否可见 </summary>
    public void SetLaneVisible(int lane, bool visible)
    {
        if (visible)
            LaneVisibilityMask |= (1 << (lane + 3));
        else
            LaneVisibilityMask &= ~(1 << (lane + 3));
    }

    /// <summary> Editor 缩放百分比（20~120），默认 100 </summary>
    [ObservableProperty]
    private int _editorZoom = 100;

    partial void OnEditorZoomChanged(int value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 判定线归一化位置（0=顶，1=底），默认 0.75 偏下 </summary>
    [ObservableProperty]
    private double _judgmentLinePosition = 0.75;

    partial void OnJudgmentLinePositionChanged(double value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    [ObservableProperty]
    private Subdivision _subdivision = Subdivision.x4;

    partial void OnSubdivisionChanged(Subdivision value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary>
    /// 从已加载的设置复制数据，不触发 Save
    /// </summary>
    public void ApplyLoaded(UserSettingsModel loaded)
    {
        _suppressSave = true;
        PixelPerSecond = loaded.PixelPerSecond;
        LaneVisibilityMask = loaded.LaneVisibilityMask;
        JudgmentLinePosition = loaded.JudgmentLinePosition;
        Subdivision = loaded.Subdivision;
        EditorZoom = loaded.EditorZoom;
        _suppressSave = false;
    }
}
