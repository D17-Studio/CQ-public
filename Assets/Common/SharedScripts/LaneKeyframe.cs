using System;

namespace CQMusicGame.Shared
{
    /// <summary>
    /// 浮点动画曲线
    /// </summary>
    public struct Curve
    {
        public int StartTime;          // 开始时间（毫秒）
        public int Duration;           // 持续时间（毫秒）
        public float StartValue;       // 开始值
        public float EndValue;         // 结束值
        public EaseCurve CurveMode;    // 缓动曲线类型
        public bool PersistEndValue;   // 终值是否影响后续

        public Curve(int startTime, int duration, float startValue, float endValue, EaseCurve curveMode)
        {
            StartTime = startTime;
            Duration = duration;
            StartValue = startValue;
            EndValue = endValue;
            CurveMode = curveMode;
            PersistEndValue = false;
        }
        
        /// <summary>
        /// 根据当前时间计算曲线值
        /// </summary>
        public float Evaluate(int currentTime)
        {
            if (currentTime <= StartTime)
                return StartValue;
            if (currentTime >= StartTime + Duration)
                return EndValue;

            float t = (currentTime - StartTime) / (float)Duration;
            float easedT = Easing.Apply(CurveMode, t);
            return StartValue + (EndValue - StartValue) * easedT;
        }

        public bool IsInCurve(int currentTime)
        {
            return currentTime >= StartTime && currentTime <= StartTime + Duration;
        }

        public bool IsInEffect(int currentTime)
        {
            if (PersistEndValue)
                return currentTime >= StartTime;
            else
                return currentTime >= StartTime && currentTime <= StartTime + Duration;
        }
    }
    
    public class LaneKeyframe
    {
        public LaneKeyframe PreCurve;
        
        public int StartTime;          // 开始时间（毫秒）
        public int Duration;           // 持续时间（毫秒）

        public Vec2 Anchor;
        
        public (Curve X,Curve Y)? PositionCurve;
        public Curve? RotationCurve;
        public Curve? HOpacityCurve;
        public Curve? LOpacityCurve;
        public Curve? KOpacityCurve;
        public Curve? NOpacityCurve;
        
        public Vec2 Position(int currentTime)
        {
            if (PositionCurve == null && PreCurve == null)
            {
                return new Vec2(0, 0);
            }
            if (PositionCurve == null || !PositionCurve.Value.X.IsInEffect(currentTime) || !PositionCurve.Value.Y.IsInEffect(currentTime))
            {
                return PreCurve.Position(currentTime);
            }
            if (Anchor is { x: 0, y: 0 } || RotationCurve == null)
            {
                return new Vec2(PositionCurve.Value.X.Evaluate(currentTime), PositionCurve.Value.Y.Evaluate(currentTime)); 
            }
            else
            {
                float r = RotationCurve.Value.Evaluate(currentTime) * (float)Math.PI / 180;
                float dx = (float)(Anchor.x * Math.Cos(r) - Anchor.y * Math.Sin(r));
                float dy = (float)(Anchor.x * Math.Sin(r) + Anchor.y * Math.Cos(r));
                float x = PositionCurve.Value.X.Evaluate(currentTime) - dx;
                float y = PositionCurve.Value.Y.Evaluate(currentTime) - dy;
                return new Vec2(x, y);
            }
        }

        public float Rotation(int currentTime)
        {
            if (RotationCurve == null  && PreCurve == null)
            {
                return 0;
            }
            if (RotationCurve == null || !RotationCurve.Value.IsInEffect(currentTime))
            {
                return  PreCurve.Rotation(currentTime);
            }
            else
            {
                return RotationCurve.Value.Evaluate(currentTime);
            }
        }

        public float HOpacity(int currentTime)
        {
            if (HOpacityCurve == null && PreCurve == null)
            {
                return 1;
            }
            if (HOpacityCurve == null || !HOpacityCurve.Value.IsInEffect(currentTime))
            {
                return PreCurve.HOpacity(currentTime);
            }
            else
            {
                return HOpacityCurve.Value.Evaluate(currentTime);
            }
        }

        public float LOpacity(int currentTime)
        {
            if (LOpacityCurve == null && PreCurve == null)
            {
                return 1;
            }
            if (LOpacityCurve == null || !LOpacityCurve.Value.IsInEffect(currentTime))
            {
                return PreCurve.LOpacity(currentTime);
            }
            else
            {
                return LOpacityCurve.Value.Evaluate(currentTime);
            }
        }

        public float KOpacity(int currentTime)
        {
            if (KOpacityCurve == null && PreCurve == null)
            {
                return 1;
            }
            if (KOpacityCurve == null || !KOpacityCurve.Value.IsInEffect(currentTime))
            {
                return PreCurve.KOpacity(currentTime);
            }
            else
            {
                return KOpacityCurve.Value.Evaluate(currentTime);
            }
        }

        public float NOpacity(int currentTime)
        {
            if (NOpacityCurve == null && PreCurve == null)
            {
                return 1;
            }
            if (NOpacityCurve == null || !NOpacityCurve.Value.IsInEffect(currentTime))
            {
                return PreCurve.NOpacity(currentTime);
            }
            else
            {
                return NOpacityCurve.Value.Evaluate(currentTime);
            }
        }
    }
}
