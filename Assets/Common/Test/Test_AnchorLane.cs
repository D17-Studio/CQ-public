using System.Collections;
using System.Collections.Generic;
using CQMusicGame.Shared;
using UnityEngine;

public class Test_AnchorLane : MonoBehaviour
{
    public int TrackTime;
    
    private LaneState _laneTransform;
    
    private SpriteRenderer _SpriteRenderer;
    
    // Start is called before the first frame update
    void Start()
    {
        _SpriteRenderer =  GetComponent<SpriteRenderer>();
        
        List<LaneMotionEffect> motions = new();
        
        motions.Add(new LaneMotionEffect()
        {
            StartTime = 0,
            LaneIndex = 0,
            Duration = 0,
            Anchor = new Vec2(0,0),
            Position = new PositionMotion()
            {
                UseRelative =  false,
                Pos = new Vec2(0,0),
                CurveMode = EaseCurve.Linear
            },
            Rotation = new RotationMotion()
            {
                UseRelative =  false,
                Angle = 0,
                CurveMode = EaseCurve.Linear
            },
            Opacity = new OpacityMotion()
            {
                AffectHead =  true,
                AffectLine =  true,
                AffectKey = true,
                Opacity = 1.0f,
                CurveMode = EaseCurve.Linear
            }
        });
        
        motions.Add(new LaneMotionEffect()
        {
            StartTime = 500,
            LaneIndex = 0,
            Duration = 500,
            Anchor = new Vec2(0,0),
            Position = new PositionMotion()
            {
                UseRelative =  false,
                Pos = new Vec2(-300,0),
                CurveMode = EaseCurve.InOutExpo
            }
        });
        
        motions.Add(new LaneMotionEffect()
        {
            StartTime = 1500,
            LaneIndex = 0,
            Duration = 500,
            Anchor = new Vec2(0,100),
            Position = new PositionMotion()
            {
                UseRelative =  true,
                Pos = new Vec2(0,300),
                CurveMode = EaseCurve.OutBack
            },
            Rotation = new RotationMotion()
            {
                UseRelative =  false,
                Angle = -90,
                CurveMode = EaseCurve.OutBack
            },
        });
        
        motions.Add(new LaneMotionEffect()
        {
            StartTime = 2500,
            LaneIndex = 0,
            Duration = 1000,
            Anchor = new Vec2(0,300),
            Position = new PositionMotion()
            {
                UseRelative =  false,
                Pos = new Vec2(0,0),
                CurveMode = EaseCurve.OutBack
            },
            Rotation = new RotationMotion()
            {
                UseRelative =  false,
                Angle = -360,
                CurveMode = EaseCurve.OutBack
            },
        });
        
        motions.Add(new LaneMotionEffect()
        {
            StartTime = 4000,
            LaneIndex = 0,
            Duration = 1000,
            Anchor = new Vec2(0,300),
            Position = new PositionMotion()
            {
                UseRelative =  true,
                Pos = new Vec2(0,0),
                CurveMode = EaseCurve.Linear
            },
            Rotation = new RotationMotion()
            {
                UseRelative =  true,
                Angle = -720,
                CurveMode = EaseCurve.InOutBack
            },
        });
        
        motions.Add(new LaneMotionEffect()
        {
            StartTime = 4300,
            LaneIndex = 0,
            Duration = 100,
            Anchor = new Vec2(0,300),
            Position = new PositionMotion()
            {
                UseRelative =  true,
                Pos = new Vec2(-1000,0),
                CurveMode = EaseCurve.Linear
            },
            Rotation = new RotationMotion()
            {
                UseRelative =  true,
                Angle = 0,
                CurveMode = EaseCurve.Linear
            },
        });
        
        _laneTransform = new LaneState(motions);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void FixedUpdate()
    {
        if (Input.GetKey(KeyCode.RightArrow))
        {
            TrackTime += 20;
        }

        if (Input.GetKey(KeyCode.LeftArrow))
        {
            TrackTime -= 20;
        }
        
        
        transform.position = new Vector3(_laneTransform.Position(0,TrackTime).x/100 ,_laneTransform.Position(0,TrackTime).y/100 ,transform.position.z);
        transform.localEulerAngles = new Vector3(0,0,_laneTransform.Rotation(0,TrackTime));
        _SpriteRenderer.color = new Color(1,1,1,_laneTransform.HeadOpacity(0,TrackTime));
    }
}
