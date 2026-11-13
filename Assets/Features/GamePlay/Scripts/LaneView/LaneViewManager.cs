using System.Collections.Generic;
using CQMusicGame.Shared;
using UnityEngine;
using Zenject;

public class LaneViewManager : MonoBehaviour
{
    [SerializeField] private LaneView _lanePrefab;

    [Inject] private TrackController _trackController;
    [Inject] private LaneState _laneTransform;
    [Inject] private ScreenAdapter _screenAdapter;
    [Inject] private ChartSheet _chart;

    private readonly List<LaneView> _laneViews = new();

    void Awake()
    {
        foreach (int laneIndex in _chart.GetLaneIndexList())
        {
            var lane = Instantiate(_lanePrefab);
            lane.Initialize(laneIndex, string.Empty, _laneTransform, _screenAdapter);
            _laneViews.Add(lane);
        }
    }

    void Update()
    {
        int trackTime = _trackController.TrackTimeMs;
        
        foreach (var laneView in _laneViews)
        {
            laneView.UpdateVisual(trackTime);
        }
    }
}
