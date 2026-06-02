using System;
using System.Collections.Generic;

namespace CQMusicGame.Shared
{
    public class LaneState
    {
        private readonly Dictionary<int, List<LaneKeyframe>> _transformCurves = new()
        {
            { -3, new List<LaneKeyframe>() },
            { -2, new List<LaneKeyframe>() },
            { -1, new List<LaneKeyframe>() },
            { 0, new List<LaneKeyframe>() },
            { 1, new List<LaneKeyframe>() },
            { 2, new List<LaneKeyframe>() },
            { 3, new List<LaneKeyframe>() },
        };
        
        #region API

        public Vec2 Position(int laneIndex,int time)
        {
            Check(laneIndex);
            return GetPosition(_transformCurves[laneIndex], time);
        }
        public float Rotation(int laneIndex,int time)
        {
            Check(laneIndex);
            return GetRotation(_transformCurves[laneIndex], time);
        }
        public float HeadOpacity(int laneIndex,int time)
        {
            Check(laneIndex);
            return GetHOpacity(_transformCurves[laneIndex], time);
        }
        public float LineOpacity(int laneIndex,int time)
        {
            Check(laneIndex);
            return GetLOpacity(_transformCurves[laneIndex], time);
        }
        public float KeyOpacity(int laneIndex,int time)
        {
            Check(laneIndex);
            return GetKOpacity(_transformCurves[laneIndex], time);
        }
        public float NoteOpacity(int laneIndex,int time)
        {
            Check(laneIndex);
            return GetNOpacity(_transformCurves[laneIndex], time);
        }

        #endregion

        public LaneState(List<LaneMotionEffect>  laneMotionEffects)
        {
            foreach (var laneMotion in laneMotionEffects)
            {
                AddCurves(laneMotion);
            }
        }

        #region 私有查询方法

        private Vec2 GetPosition(List<LaneKeyframe> curves, int time)
        {
            for (int i = curves.Count-1; i >= 0; i--)
            {
                if (curves[i].StartTime <= time)
                {
                    return curves[i].Position(time);
                }
            }
            return new Vec2(0,0);
        }
        
        private float GetRotation(List<LaneKeyframe> curves, int time)
        {
            for (int i = curves.Count-1; i >= 0; i--)
            {
                if (curves[i].StartTime <= time)
                {
                    return curves[i].Rotation(time);
                }
            }
            return 0;
        }
        
        private float GetHOpacity(List<LaneKeyframe> curves, int time)
        {
            for (int i = curves.Count-1; i >= 0; i--)
            {
                if (curves[i].StartTime <= time)
                {
                    return curves[i].HOpacity(time);
                }
            }
            return 1;
        }

        private float GetLOpacity(List<LaneKeyframe> curves, int time)
        {
            for (int i = curves.Count-1; i >= 0; i--)
            {
                if (curves[i].StartTime <= time)
                {
                    return curves[i].LOpacity(time);
                }
            }
            return 1;
        }

        private float GetKOpacity(List<LaneKeyframe> curves, int time)
        {
            for (int i = curves.Count-1; i >= 0; i--)
            {
                if (curves[i].StartTime <= time)
                {
                    return curves[i].KOpacity(time);
                }
            }
            return 1;
        }

        private float GetNOpacity(List<LaneKeyframe> curves, int time)
        {
            for (int i = curves.Count-1; i >= 0; i--)
            {
                if (curves[i].StartTime <= time)
                {
                    return curves[i].NOpacity(time);
                }
            }
            return 1;
        }
        
        private bool DoPersistPosition(List<LaneKeyframe> curves, int time)
        {
            for (int i = curves.Count - 1; i >= 0; i--)
            {
                if (curves[i].StartTime <= time && curves[i].StartTime + curves[i].Duration >= time && curves[i].PositionCurve != null)
                    return false;
            }
            return true;
        }
        
        private bool DoPersistRotation(List<LaneKeyframe> curves, int time)
        {
            for (int i = curves.Count - 1; i >= 0; i--)
            {
                if (curves[i].StartTime <= time && curves[i].StartTime + curves[i].Duration >= time && curves[i].RotationCurve != null)
                    return false;
            }
            return true;
        }
        
        private bool DoPersistHOpacity(List<LaneKeyframe> curves, int time)
        {
            for (int i = curves.Count - 1; i >= 0; i--)
            {
                if (curves[i].StartTime <= time && curves[i].StartTime + curves[i].Duration >= time && curves[i].HOpacityCurve != null)
                    return false;
            }
            return true;
        }
        
        private bool DoPersistLOpacity(List<LaneKeyframe> curves, int time)
        {
            for (int i = curves.Count - 1; i >= 0; i--)
            {
                if (curves[i].StartTime <= time && curves[i].StartTime + curves[i].Duration >= time && curves[i].LOpacityCurve != null)
                    return false;
            }
            return true;
        }
        
        private bool DoPersistKOpacity(List<LaneKeyframe> curves, int time)
        {
            for (int i = curves.Count - 1; i >= 0; i--)
            {
                if (curves[i].StartTime <= time && curves[i].StartTime + curves[i].Duration >= time && curves[i].KOpacityCurve != null)
                    return false;
            }
            return true;
        }

        private bool DoPersistNOpacity(List<LaneKeyframe> curves, int time)
        {
            for (int i = curves.Count - 1; i >= 0; i--)
            {
                if (curves[i].StartTime <= time && curves[i].StartTime + curves[i].Duration >= time && curves[i].NOpacityCurve != null)
                    return false;
            }
            return true;
        }

        #endregion
        
        private void AddCurves(LaneMotionEffect laneMotion)
        {
            Check(laneMotion.LaneIndex);
            List<LaneKeyframe> curves = _transformCurves[laneMotion.LaneIndex];
            
            LaneKeyframe transformCurve = new()
            {
                StartTime = laneMotion.StartTime,
                Duration = laneMotion.Duration,
                Anchor = laneMotion.Anchor
            };
           
            Curve sampleCurve = new()
            {
                StartTime = laneMotion.StartTime,
                Duration = laneMotion.Duration
            };
            
            int startTime =  laneMotion.StartTime;
            int endTime =  laneMotion.StartTime +  laneMotion.Duration;
            

            if (laneMotion.Position != null)
            {
                Curve pCurve = sampleCurve;
                pCurve.CurveMode = laneMotion.Position.Value.CurveMode;
                pCurve.PersistEndValue = DoPersistPosition(curves,endTime);
                
                float r = GetRotation(curves,startTime) * (float)Math.PI / 180;
                float dx = (float)(laneMotion.Anchor.x * Math.Cos(r) - laneMotion.Anchor.y * Math.Sin(r));
                float dy = (float)(laneMotion.Anchor.x * Math.Sin(r) + laneMotion.Anchor.y * Math.Cos(r));
                
                //添加X轴位置曲线
                Curve xCurve = pCurve;
                xCurve.StartValue = GetPosition(curves, startTime).x;
                xCurve.StartValue += dx;
                xCurve.EndValue = laneMotion.Position.Value.Pos.x;
                if (laneMotion.Position.Value.UseRelative)
                    xCurve.EndValue += xCurve.StartValue;
                
                //添加Y轴位置曲线
                Curve yCurve = pCurve;
                yCurve.StartValue = GetPosition(curves, startTime).y;
                yCurve.StartValue += dy;
                yCurve.EndValue = laneMotion.Position.Value.Pos.y;
                if (laneMotion.Position.Value.UseRelative)
                    yCurve.EndValue += yCurve.StartValue;
                
                
          
             
          
         
                transformCurve.PositionCurve = (xCurve, yCurve);
            }

            if (laneMotion.Rotation != null)
            {
                //添加旋转曲线
                Curve rCurve = sampleCurve;
                rCurve.StartValue  = GetRotation(curves, startTime);
                rCurve.EndValue = laneMotion.Rotation.Value.Angle;
                if (laneMotion.Rotation.Value.UseRelative)
                    rCurve.EndValue += rCurve.StartValue;
                rCurve.CurveMode = laneMotion.Rotation.Value.CurveMode;
                rCurve.PersistEndValue = DoPersistRotation(curves,endTime);
                transformCurve.RotationCurve = rCurve;
            }

            if (laneMotion.Opacity != null)
            {
                Curve oCurve = sampleCurve;
                oCurve.EndValue = laneMotion.Opacity.Value.Opacity;
                oCurve.CurveMode = laneMotion.Opacity.Value.CurveMode;
                
                if (laneMotion.Opacity.Value.AffectHead)
                {
                    Curve hCurve = oCurve;
                    hCurve.StartValue = GetHOpacity(curves, startTime);
                    hCurve.PersistEndValue = DoPersistHOpacity(curves,endTime);
                    transformCurve.HOpacityCurve = hCurve;
                }

                if (laneMotion.Opacity.Value.AffectLine)
                {
                    Curve lCurve = oCurve;
                    lCurve.StartValue = GetLOpacity(curves, startTime);
                    lCurve.PersistEndValue = DoPersistLOpacity(curves,endTime);
                    transformCurve.LOpacityCurve = lCurve;
                }

                if (laneMotion.Opacity.Value.AffectKey)
                {
                    Curve kCurve = oCurve;
                    kCurve.StartValue = GetKOpacity(curves, startTime);
                    kCurve.PersistEndValue = DoPersistKOpacity(curves,endTime);
                    transformCurve.KOpacityCurve = kCurve;
                }

                if (laneMotion.Opacity.Value.AffectNote)
                {
                    Curve nCurve = oCurve;
                    nCurve.StartValue = GetNOpacity(curves, startTime);
                    nCurve.PersistEndValue = DoPersistNOpacity(curves,endTime);
                    transformCurve.NOpacityCurve = nCurve;
                }
            }

            if (curves.Count > 0)
            {
                transformCurve.PreCurve = curves[^1];
            }
            
            curves.Add(transformCurve);
        }
        
        private bool Check(int laneIndex)
        {
            if (laneIndex < -3 || laneIndex > 3)
            {
                Console.WriteLine("LaneIndex out of range");
                return false;
            }
            return true;
        }
    }
}