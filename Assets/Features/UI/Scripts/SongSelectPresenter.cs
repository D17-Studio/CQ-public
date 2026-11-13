using System.Collections.Generic;
using UnityEngine;
using Zenject;

/// <summary>
/// 选歌页大脑（纯 C#，由 MenuSceneInstaller 装配）。持浏览状态（光标索引 / 当前难度）与步进节流。
/// Tick 轮询键盘：↑↓ 切歌、←→ 切难度；点开始写 PlaySessionService 选曲 → 进打歌。
/// 难度规则：切歌时按"就近向低"回退；点难度只采纳有谱项；←→ 步进（无谱跳过、两端钳制）。
/// </summary>
public class SongSelectPresenter : IInitializable, ITickable
{
    // 难度数量 = ChartDif 枚举项数
    private const int DifficultyCount = (int)ChartDif.EX + 1;

    // 步进节流间隔（秒）：平滑滚轮 / 触控板会连发事件，防一滚多格
    private const float StepCooldown = 0.08f;

    private readonly TrackCollection _trackCollection;
    private readonly PlaySessionService _playSessionService;
    private readonly MenuNavigator _navigator;
    private readonly ScoreArchive _archive;
    private readonly InputService _inputService;
    private readonly MusicService _musicService;
    private readonly SongSelectView _view;

    // 浏览状态：曲目列表 / 光标索引 / 当前难度。
    private IReadOnlyList<TrackData> _tracks;
    private int _cursorIndex;
    private ChartDif _selectedDifficulty;

    // 上次触发试听的曲目（按引用比较，避免难度变化等场景重复播放）。
    private TrackData _previewedTrack;

    // 节流时刻（滚轮与方向键共用；unscaled 时间）
    private float _lastStepTime = float.NegativeInfinity;
    private float _lastDifficultyStepTime = float.NegativeInfinity;

    // 难度态下推缓冲（复用）。
    private readonly DifficultyState[] _difficultyStates = new DifficultyState[DifficultyCount];

    public SongSelectPresenter(TrackCollection trackCollection,
                               PlaySessionService selectionService,
                               MenuNavigator navigator,
                               ScoreArchive archive,
                               InputService inputService,
                               MusicService musicService,
                               SongSelectView view)
    {
        _trackCollection = trackCollection;
        _playSessionService = selectionService;
        _navigator = navigator;
        _archive = archive;
        _inputService = inputService;
        _musicService = musicService;
        _view = view;
    }

    /// <summary>初始化：全量曲目下推轮盘；光标与难度恢复到上次选曲；下推首屏难度态。</summary>
    public void Initialize()
    {
        _tracks = _trackCollection.GetTracks();
        _view.SetTracks(_tracks);

        _cursorIndex = IndexOfSelectedTrack();
        _view.SetCursor(_cursorIndex);

        if (_playSessionService.SelectedChart != null)
            _selectedDifficulty = _playSessionService.SelectedChart.Difficulty;

        RefreshDifficultyStates();
        _view.SetAutoPlay(_playSessionService.AutoPlay);

        _view.TrackClicked += OnTrackClicked;
        _view.StepRequested += OnStepRequested;
        _view.DifficultyClicked += OnDifficultyClicked;
        _view.StartClicked += OnStartClicked;
        _view.SettingsClicked += OnSettingsClicked;
        _view.AutoPlayToggled += OnAutoPlayToggled;
        _navigator.SettingsClosed += OnSettingsClosed;

        // 首次进菜单 / 从打歌场景返回：自动试听当前选中曲。
        PlayPreviewForCurrentTrack();
    }

    /// <summary>每帧轮询键盘方向键：↑↓ 切歌、←→ 切难度。</summary>
    public void Tick()
    {
        if (_inputService.MenuUp()) OnStepRequested(-1);
        if (_inputService.MenuDown()) OnStepRequested(1);
        if (_inputService.MenuLeft()) OnDifficultyStepRequested(-1);
        if (_inputService.MenuRight()) OnDifficultyStepRequested(1);
    }

    /// <summary>反查上次选中曲目在列表中的索引（按引用比较）；找不到回 0。</summary>
    private int IndexOfSelectedTrack()
    {
        var selected = _playSessionService.SelectedTrack;
        if (selected != null)
        {
            for (int i = 0; i < _tracks.Count; i++)
            {
                if (ReferenceEquals(_tracks[i], selected)) return i;
            }
        }
        return 0;
    }

    /// <summary>点格切歌：切到被点曲目并居中，随后刷新难度栏。</summary>
    private void OnTrackClicked(int trackIndex)
    {
        if (_navigator.IsPopupOpen) return;   // 弹层打开时屏蔽选歌页
        _cursorIndex = trackIndex;
        _view.SetCursor(_cursorIndex);
        RefreshDifficultyStates();
        PlayPreviewForCurrentTrack();
    }

    /// <summary>切歌步进：节流后光标 ±1、首尾钳制（无环绕），刷新难度栏。</summary>
    private void OnStepRequested(int delta)
    {
        if (_navigator.IsPopupOpen) return;
        if (Time.unscaledTime - _lastStepTime < StepCooldown) return;
        _lastStepTime = Time.unscaledTime;

        if (_tracks.Count == 0) return;

        int nextIndex = Mathf.Clamp(_cursorIndex + delta, 0, _tracks.Count - 1);
        if (nextIndex == _cursorIndex) return;   // 首尾钳制：没换曲不重播试听

        _cursorIndex = nextIndex;
        _view.SetCursor(_cursorIndex);
        RefreshDifficultyStates();
        PlayPreviewForCurrentTrack();
    }

    /// <summary>点难度：只采纳有谱项。</summary>
    private void OnDifficultyClicked(ChartDif difficulty)
    {
        if (_navigator.IsPopupOpen) return;
        if (_tracks.Count == 0) return;
        if (FindChart(_tracks[_cursorIndex], difficulty) == null) return;

        _selectedDifficulty = difficulty;
        RefreshDifficultyStates();
    }

    /// <summary>←→ 步进切难度：向该方向找最近有谱的难度；没有则停在原地（无循环）。</summary>
    private void OnDifficultyStepRequested(int delta)
    {
        if (_navigator.IsPopupOpen) return;
        if (Time.unscaledTime - _lastDifficultyStepTime < StepCooldown) return;
        _lastDifficultyStepTime = Time.unscaledTime;

        if (_tracks.Count == 0) return;

        var track = _tracks[_cursorIndex];
        for (int i = (int)_selectedDifficulty + delta; i >= 0 && i < DifficultyCount; i += delta)
        {
            if (FindChart(track, (ChartDif)i) != null)
            {
                _selectedDifficulty = (ChartDif)i;
                RefreshDifficultyStates();
                return;
            }
        }
    }

    /// <summary>点设置：先停试听，再交给导航器打开弹层。</summary>
    private void OnSettingsClicked()
    {
        _musicService.Stop();
        _navigator.OpenSettings();
    }

    /// <summary>设置关闭：恢复当前选中曲的试听。</summary>
    private void OnSettingsClosed() => PlayPreviewForCurrentTrack(force: true);

    /// <summary>切换 AutoPlay：取反写回本局会话服务并下推开关态。弹层打开时屏蔽。</summary>
    private void OnAutoPlayToggled()
    {
        if (_navigator.IsPopupOpen) return;

        _playSessionService.AutoPlay = !_playSessionService.AutoPlay;
        _view.SetAutoPlay(_playSessionService.AutoPlay);
    }

    /// <summary>点开始：写 PlaySessionService 选曲，载入打歌场景。</summary>
    private void OnStartClicked()
    {
        if (_navigator.IsPopupOpen) return;
        if (_tracks.Count == 0) return;

        var chart = FindChart(_tracks[_cursorIndex], _selectedDifficulty);
        if (chart == null) return;   // 整曲无谱兜底

        _playSessionService.SelectedTrack = _tracks[_cursorIndex];
        _playSessionService.SelectedChart = chart;

        _musicService.Stop();   // 避免试听残留 / 与开局音乐重叠
        _navigator.EnterGame();
    }

    /// <summary>切歌试听：仅曲目变化时触发；官谱 / 自制谱共用统一预览。</summary>
    /// <param name="force">true = 同曲也重播（设置关闭恢复试听用）。</param>
    private void PlayPreviewForCurrentTrack(bool force = false)
    {
        if (_tracks.Count == 0) return;

        var track = _tracks[_cursorIndex];
        if (!force && ReferenceEquals(track, _previewedTrack)) return;
        _previewedTrack = track;

        _musicService.PlayPreview(track);
    }

    /// <summary>刷新当前曲：选中难度无谱时按"就近向低"回退；下推 4 条难度态 + 详情区 + 开始按钮态。</summary>
    private void RefreshDifficultyStates()
    {
        if (_tracks.Count == 0) return;

        var track = _tracks[_cursorIndex];

        if (FindChart(track, _selectedDifficulty) == null)
        {
            var fallback = FindFallbackDifficulty(track);
            if (fallback.HasValue) _selectedDifficulty = fallback.Value;
        }

        for (int i = 0; i < DifficultyCount; i++)
        {
            var difficulty = (ChartDif)i;
            var chart = FindChart(track, difficulty);
            _difficultyStates[i] = new DifficultyState(difficulty,
                                                       chart != null,
                                                       chart?.ChartConstant ?? 0,
                                                       chart != null && difficulty == _selectedDifficulty);
        }

        var selectedChart = FindChart(track, _selectedDifficulty);
        var best = selectedChart != null ? _archive.GetBest(ScoreKeys.Of(track, selectedChart)) : default;
        _view.SetDifficultyStates(_difficultyStates);
        _view.SetDetail(track, selectedChart, best);
        _view.SetStartInteractable(selectedChart != null);
    }

    /// <summary>在曲目的谱面列表里找指定难度；没有则回 null。</summary>
    private static ChartData FindChart(TrackData track, ChartDif difficulty)
    {
        foreach (var chart in track.Charts)
        {
            if (chart.Difficulty == difficulty) return chart;
        }
        return null;
    }

    /// <summary>就近向低回退：先向低找最近有谱的，全无更低的才向高找；整曲无谱回 null。</summary>
    private ChartDif? FindFallbackDifficulty(TrackData track)
    {
        for (int i = (int)_selectedDifficulty - 1; i >= 0; i--)
        {
            if (FindChart(track, (ChartDif)i) != null) return (ChartDif)i;
        }

        for (int i = (int)_selectedDifficulty + 1; i < DifficultyCount; i++)
        {
            if (FindChart(track, (ChartDif)i) != null) return (ChartDif)i;
        }

        return null;
    }
}
