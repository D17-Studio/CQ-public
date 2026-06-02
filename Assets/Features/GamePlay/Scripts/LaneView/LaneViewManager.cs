using System.Collections;
using System.Collections.Generic;
using CQMusicGame.Shared;
using UnityEngine;
using Zenject;

public class LaneViewManager : MonoBehaviour
{
    [SerializeField] private LaneView _lanePrefab;
    [SerializeField] private RectTransform _parent; 
    
    [Inject] private TrackController _trackController;
    [Inject] private LaneState _laneTransform;

    private readonly List<LaneView> _laneViews = new();
    
    void Awake()
    {
        for (int i = 0; i < 7; i++)
        {
            string keyText = KeyNameDisplay.Format(InputSystem.Instance.GetLineBindingName(i - 3));
            
            var lane = Instantiate(_lanePrefab, _parent);
            lane.Initialize(i - 3, keyText, _laneTransform);
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
