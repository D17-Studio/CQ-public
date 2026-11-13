using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 选歌页轮盘视图（挂 SongSelectPanel）：持 cell 池与轮盘采样（唯一动画 = 一个 cursor 浮点）；
/// 点格 / 难度 / 开始 / 设置 / AutoPlay 开关点击上抛；详情区、难度栏、开始按钮、开关态由 Presenter 下推。
/// </summary>
public class SongSelectView : MonoBehaviour, IScrollHandler
{
    // ── 面板引用 ──

    // 轮盘容器（TrackList）：池的父节点，格子坐标以它为中心。
    [SerializeField] private RectTransform _cellContainer;
    // 格子模板（SongCell.prefab）。
    [SerializeField] private SongCellView _cellPrefab;
    // 位置指示条。
    [SerializeField] private Scrollbar _positionIndicator;

    // 槽位表：每槽 {X, Y, Scale, Alpha}；索引 0 = 最上，正中一条 = 光标所在槽。
    // 最外两条 = 出场位（Alpha 0 = 回收线）；数值可增删 / 非对称。
    [SerializeField] private Slot[] _slots =
    {
        new Slot { PosX = 0f, PosY =  400f, Scale = 0.62f, Alpha = 0f },
        new Slot { PosX = 0f, PosY =  400f, Scale = 0.70f, Alpha = 1f },
        new Slot { PosX = 0f, PosY =  300f, Scale = 0.78f, Alpha = 1f },
        new Slot { PosX = 0f, PosY =  170f, Scale = 0.88f, Alpha = 1f },
        new Slot { PosX = 0f, PosY =    0f, Scale = 1.00f, Alpha = 1f },
        new Slot { PosX = 0f, PosY = -170f, Scale = 0.88f, Alpha = 1f },
        new Slot { PosX = 0f, PosY = -300f, Scale = 0.78f, Alpha = 1f },
        new Slot { PosX = 0f, PosY = -400f, Scale = 0.70f, Alpha = 1f },
        new Slot { PosX = 0f, PosY = -400f, Scale = 0.62f, Alpha = 0f },
    };

    // 中心段 X / Scale 缓动——进中心方向（> 1 越大越早）。
    [SerializeField] private float _centerEnterEase = 2f;
    // 中心段 X / Scale 缓动——出中心方向（< 1 越小越早）。
    [SerializeField] private float _centerLeaveEase = 0.5f;

    // 光标收敛速度。
    [SerializeField] private float _cursorSmoothSpeed = 12f;

    [Serializable]
    private struct Slot
    {
        public float PosX;
        public float PosY;
        public float Scale;
        public float Alpha;
    }

    // ── 难度栏 ──

    // 难度按钮视图（下标顺序 = EZ / ST / HD / EX）。
    [Header("难度栏 — 按钮（下标顺序 = EZ / ST / HD / EX）")]
    [SerializeField] private DifficultyButtonView[] _difficultyButtons = new DifficultyButtonView[4];

    // ── 详情区 / 开始按钮 ──

    // 封面图（曲目无封面时 sprite 为空）。
    [SerializeField] private Image _coverImage;
    // 详情文本（内容由本类拼装）。
    [SerializeField] private TMP_Text _detailText;

    // 开始按钮。
    [SerializeField] private Button _startButton;
    // 设置按钮。
    [SerializeField] private Button _settingsButton;
    // AutoPlay 开关。
    [SerializeField] private AutoPlayToggleView _autoPlayToggle;

    // 池实例条目：绑定的曲目索引（-1 = 空闲）。
    private sealed class PooledCell
    {
        public SongCellView View;
        public int TrackIndex = -1;
    }

    private readonly List<PooledCell> _pool = new();
    private IReadOnlyList<TrackData> _tracks;

    // 光标：浮点 = 轮盘当前位置；整数目标 = 选中曲目序号。
    private float _cursor;
    private int _cursorTarget;
    private float _cursorVelocity;   // 本帧光标速度（判断进 / 出中心方向用）

    private int CenterIndex => (_slots.Length - 1) / 2;

    /// <summary>点了某曲（携带曲目索引）。</summary>
    public event Action<int> TrackClicked;

    /// <summary>请求切歌步进（滚轮上抛）：+1 = 后一曲、−1 = 前一曲。</summary>
    public event Action<int> StepRequested;

    /// <summary>点了某个难度按钮（携带难度）。</summary>
    public event Action<ChartDif> DifficultyClicked;

    /// <summary>点了开始按钮。</summary>
    public event Action StartClicked;

    /// <summary>点了设置按钮。</summary>
    public event Action SettingsClicked;

    /// <summary>点了 AutoPlay 开关。</summary>
    public event Action AutoPlayToggled;

    private void Awake()
    {
        if (_difficultyButtons != null)
        {
            // 数组下标即 ChartDif 枚举序
            for (int i = 0; i < _difficultyButtons.Length; i++)
            {
                var buttonView = _difficultyButtons[i];
                if (buttonView == null) continue;

                int difficultyIndex = i;   // 闭包捕获副本
                buttonView.Clicked += () => DifficultyClicked?.Invoke((ChartDif)difficultyIndex);
            }
        }

        if (_startButton != null)
        {
            _startButton.onClick.AddListener(() => StartClicked?.Invoke());
        }

        if (_settingsButton != null)
        {
            _settingsButton.onClick.AddListener(() => SettingsClicked?.Invoke());
        }

        if (_autoPlayToggle != null)
        {
            _autoPlayToggle.Clicked += () => AutoPlayToggled?.Invoke();
        }
    }

    private void Update()
    {
        if (_tracks == null || _tracks.Count == 0) return;

        float prevCursor = _cursor;

        // 光标向目标平滑收敛
        _cursor = Mathf.Lerp(_cursor, _cursorTarget, 1f - Mathf.Exp(-_cursorSmoothSpeed * Time.deltaTime));
        if (Mathf.Abs(_cursor - _cursorTarget) < 0.001f) _cursor = _cursorTarget;

        _cursorVelocity = _cursor - prevCursor;
        UpdateWheel();
    }

    // ── 数据口（Presenter 下推） ──

    /// <summary>接收全部曲目：建池 + 初始绑定。</summary>
    public void SetTracks(IReadOnlyList<TrackData> tracks)
    {
        _tracks = tracks;

        foreach (var cell in _pool) Destroy(cell.View.gameObject);
        _pool.Clear();

        // 池 = 槽位表条数 + 1（跨界瞬时缓冲）
        for (int i = 0; i < _slots.Length + 1; i++)
        {
            var cell = new PooledCell { View = Instantiate(_cellPrefab, _cellContainer, false) };
            cell.View.gameObject.SetActive(false);
            cell.View.Clicked += () => OnCellClicked(cell);
            _pool.Add(cell);
        }

        if (tracks.Count > 0) UpdateWheel();
    }

    /// <summary>把光标目标切到指定曲目索引（= 选中该曲），平滑补间。</summary>
    public void SetCursor(int trackIndex)
    {
        if (_tracks == null || _tracks.Count == 0) return;
        _cursorTarget = trackIndex;
    }

    /// <summary>难度栏下推：每条难度态（有谱 / 无谱 + 定数 + 是否选中）转给对应按钮视图（states 按 ChartDif 枚举序）。</summary>
    public void SetDifficultyStates(IReadOnlyList<DifficultyState> states)
    {
        if (_difficultyButtons == null || states == null) return;

        for (int i = 0; i < states.Count; i++)
        {
            var state = states[i];
            int index = (int)state.Difficulty;
            if (index < 0 || index >= _difficultyButtons.Length) continue;

            var buttonView = _difficultyButtons[index];
            if (buttonView == null) continue;

            buttonView.SetState(state.HasChart, state.Constant, state.Selected);
        }
    }

    /// <summary>
    /// 详情区下推：成绩行 + 封面 + 曲名 / Composer / Charter / BPM。
    /// best 无记录时只显零分；chart 为 null 时谱师显示占位。
    /// </summary>
    public void SetDetail(TrackData track, ChartData chart, ScoreView best)
    {
        _coverImage.sprite = track.MusicCover;

        string bpm = track.BPM > 0 ? track.BPM.ToString() : "—";
        string score = best.BestScore.ToString("D7");
        string evaluate = best.Exists ? $"_{ScoringSystem.GetEvaluateStatic(best.BestScore)}" : "";
        string isFC = best.IsFullCombo ? "_FC" : "";
        _detailText.text =
            $"{score}{evaluate}{isFC} \n" +
            $"{track.MusicTitle}\n" +
            $"Composer:{OrDash(track.MusicArtist)}\n" +
            $"Charter:{OrDash(chart?.ChartArtist)}\n" +
            $"BPM:{bpm}";
    }

    /// <summary>开始按钮可交互态。</summary>
    public void SetStartInteractable(bool interactable)
    {
        _startButton.interactable = interactable;
    }

    /// <summary>AutoPlay 开关态下推。</summary>
    public void SetAutoPlay(bool isOn)
    {
        if (_autoPlayToggle != null) _autoPlayToggle.SetOn(isOn);
    }

    private static string OrDash(string value) => string.IsNullOrEmpty(value) ? "—" : value;

    // ── 轮盘内部 ──

    private void OnCellClicked(PooledCell cell)
    {
        if (cell.TrackIndex >= 0) TrackClicked?.Invoke(cell.TrackIndex);
    }

    /// <summary>滚轮切歌：上滚 = 前一曲。</summary>
    public void OnScroll(PointerEventData eventData)
    {
        if (eventData.scrollDelta.y > 0.01f) StepRequested?.Invoke(-1);
        else if (eventData.scrollDelta.y < -0.01f) StepRequested?.Invoke(1);
    }

    private void UpdateWheel()
    {
        int count = _tracks.Count;
        int last = _slots.Length - 1;

        // 可视窗：两侧各延伸到最外槽
        float leftBound = _cursor - CenterIndex;
        float rightBound = _cursor + (last - CenterIndex);

        // ① 出窗实例：完全透明才释放（可见格永不换内容）
        foreach (var cell in _pool)
        {
            if (cell.TrackIndex < 0) continue;
            if (cell.TrackIndex > leftBound && cell.TrackIndex < rightBound) continue;
            if (SampleRelative(cell.TrackIndex - _cursor).Alpha > 0f) continue;

            cell.TrackIndex = -1;
            cell.View.gameObject.SetActive(false);
        }

        // ② 窗内缺的索引：取空闲实例换绑（裁剪到 0..count-1）
        int from = Mathf.Max(0, Mathf.FloorToInt(leftBound) + 1);
        int to = Mathf.Min(count - 1, Mathf.CeilToInt(rightBound) - 1);
        for (int i = from; i <= to; i++)
        {
            if (IsBound(i)) continue;

            var cell = GetFreeCell();
            if (cell == null) continue;   // 不应发生：池 = 槽数 + 1

            cell.TrackIndex = i;
            cell.View.gameObject.SetActive(true);
            cell.View.Bind(_tracks[i]);
        }

        // ③ 每帧采样 → 写布局与选中表现
        foreach (var cell in _pool)
        {
            if (cell.TrackIndex < 0) continue;

            float relative = cell.TrackIndex - _cursor;
            var s = SampleRelative(relative);
            cell.View.SetLayout(s.PosX, s.PosY, s.Scale, s.Alpha);
            cell.View.SetSelected(Mathf.Clamp01(1f - Mathf.Abs(relative)));
        }

        // ④ 指示条
        _positionIndicator.value = count > 1 ? Mathf.Clamp01(_cursor / (count - 1)) : 0f;
    }

    private bool IsBound(int trackIndex)
    {
        foreach (var cell in _pool)
            if (cell.TrackIndex == trackIndex) return true;
        return false;
    }

    private PooledCell GetFreeCell()
    {
        foreach (var cell in _pool)
            if (cell.TrackIndex < 0) return cell;
        return null;
    }

    /// <summary>把"相对光标的有符号距离"采样成（X / Y / 缩放 / 出场透明度）。</summary>
    private (float PosX, float PosY, float Scale, float Alpha) SampleRelative(float relative)
    {
        int last = _slots.Length - 1;
        float t = Mathf.Clamp(relative + CenterIndex, 0f, last);
        int i0 = Mathf.Min((int)t, last - 1);
        float frac = t - i0;

        // 贴中心的两段（|rel| ≤ 1）：X / Scale 按进 / 出中心方向取缓动；Y / Alpha 保持线性
        float easedFrac = frac;
        if (i0 == CenterIndex || i0 == CenterIndex - 1)
        {
            float u = Mathf.Abs(relative);                    // 0 = 中心，1 = 邻位
            bool entering = relative * _cursorVelocity > 0f;  // 正在靠近中心
            float neighborness = Mathf.Pow(u, entering ? _centerEnterEase : _centerLeaveEase);
            easedFrac = i0 == CenterIndex ? neighborness : 1f - neighborness;
        }

        return (Mathf.Lerp(_slots[i0].PosX, _slots[i0 + 1].PosX, easedFrac),
                Mathf.Lerp(_slots[i0].PosY, _slots[i0 + 1].PosY, frac),
                Mathf.Lerp(_slots[i0].Scale, _slots[i0 + 1].Scale, easedFrac),
                Mathf.Lerp(_slots[i0].Alpha, _slots[i0 + 1].Alpha, frac));
    }
}
