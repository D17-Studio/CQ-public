using System;
using System.Collections;
using System.Collections.Generic;
using CQMusicGame.Shared;
using UnityEngine;

public class Test_NoteVFX : MonoBehaviour
{
    private Test_NotePosition _notePosition;
    private NotePositionCalculator _notePositionCalculator;
    private NoteData _noteData;
    
    public void Initialize(NoteData noteData, NotePositionCalculator notePositionCalculator,
        Test_NotePosition notePosition)
    {
        _notePosition = notePosition;
        _notePositionCalculator = notePositionCalculator;
        _noteData = noteData;

        if (_noteData.Type == NoteType.Mute)
        {
            _spriteRenderer.enabled = false;
        }
    }

    public GameObject[] lines;
    
    private List<Vec2> _points = new();

    private SpriteRenderer _spriteRenderer;

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        if (!_notePosition
            && _notePositionCalculator == null)
            return;
        UpdatePosition();
        if (_notePosition.T_TrackTimeMs > _noteData.TargetTime + _noteData.SustainDuration)
        {
            transform.position = new Vector3(-1000, -1000, -1000);
        }
    }

    private void UpdatePosition()
    {
        _notePositionCalculator.Calculate(_noteData, _notePosition.T_TrackTimeMs, _points);
        
        transform.position = new Vector3(_points[0].x , _points[0].y , 0)/1080*9;
        
        if (_points.Count == 1)
            return;
        
        for (int i = 0; i < _points.Count -1; i++)
        {
            Vector2 thisPos = new Vector3(_points[i].x, _points[i].y, 0) / 1080 * 9;
            Vector2 targetPos = new Vector3(_points[i + 1].x, _points[i + 1].y, 0) / 1080 * 9;
            
            SetLine(lines[i].transform, targetPos, thisPos);
        }
    }

    private void SetLine(Transform line, Vector2 targetPos, Vector2 thisPos)
    {
        line.transform.position =thisPos+ (targetPos -  thisPos)/2;
        line.transform.up = targetPos -  thisPos;
        line.transform.localScale = new Vector3(line.transform.localScale.x, (targetPos -  thisPos).magnitude/transform.localScale.y, line.transform.localScale.z);
    }
    
}
