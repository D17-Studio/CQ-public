using System;
using System.Collections;
using System.Collections.Generic;
using CQMusicGame.Shared;
using UnityEngine;
using Zenject;

public class Test_NotePosition : MonoBehaviour
{
    [Inject] private ChartSheet _chart;
    [Inject] private LaneState  _laneTransform;

    private NotePositionCalculator _notePositionCalculator;

    public GameObject[] Lanes;
    
    public GameObject notePrefab;

    public int T_TrackTimeMs;
    

    private void Start()
    {
        _notePositionCalculator = new NotePositionCalculator(1000, _laneTransform);

        foreach (var note in _chart.GetNotes())
        {
            var noteObj = GameObject.Instantiate(notePrefab);
            var noteVFX = noteObj.GetComponent<Test_NoteVFX>();
            noteVFX.Initialize(note, _notePositionCalculator,this);
        }
    }
    
    private void Update()
    {
        if (Input.GetKey(KeyCode.RightArrow))
        {
            T_TrackTimeMs += (int)(Time.deltaTime*1000);
        }
        if (Input.GetKey(KeyCode.LeftArrow))
        {
            T_TrackTimeMs -= (int)(Time.deltaTime*1000);
        }
        
        
        //计算轨道位置
        for (int i = 0; i < 7; i++)
        {
            Vec2 pos = _laneTransform.Position(i-3,T_TrackTimeMs);
            Lanes[i].transform.position = new Vector3(pos.x, pos.y, Lanes[i].transform.position.z)/1080*9;
            Lanes[i].transform.localEulerAngles = new Vector3(0, 0, _laneTransform.Rotation(i-3,T_TrackTimeMs));
        }
    }
}
