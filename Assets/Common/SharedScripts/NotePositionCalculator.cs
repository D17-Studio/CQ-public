using System.Collections.Generic;

namespace CQMusicGame.Shared
{
    /// <summary>
    /// 计算Note的位置属性
    /// </summary>
    public class NotePositionCalculator
    {
        public float GlobalSpeed { get; set; }
        private readonly LaneState _laneTransform;
        
        private List<CalInfo> _calInfos = new();

        public NotePositionCalculator(float globalSpeed, LaneState laneTransform)
        {
            GlobalSpeed = globalSpeed;
            _laneTransform = laneTransform;
        }
        
        private struct CalInfo
        {
            public int TrackTime;
            public int PointTime;
            public int LaneIndex;
            public float Speed;
            public bool DoPosAbsolute;
            public Vec2 Pos;
        }

        /// <summary>
        /// 计算Note位置
        /// </summary>
        /// <param name="note">Note信息</param>
        /// <param name="trackTime">乐曲时间</param>
        /// <param name="outPoints">输出列表</param>
        public void Calculate(NoteData note, int trackTime, List<Vec2> outPoints)
        {
            outPoints.Clear();
            _calInfos.Clear();
            
            CalInfo calInfo = new()
            {
                TrackTime = trackTime,
                PointTime = note.TargetTime,
                LaneIndex = note.LaneIndex,
                Speed = ResolveSpeed(note),
                DoPosAbsolute = false,
                Pos = Vec2.Zero
            };
            
            _calInfos.Add(calInfo);

            if (note.Type == NoteType.Dot)
            {
                outPoints.Add(CalculatePoint(calInfo));
                return;
            }
            
            if (note.Type == NoteType.Mute)
            {
                calInfo.PointTime = note.TargetTime + note.SustainDuration;
                _calInfos.Add(calInfo);
            }

            if (note.Type == NoteType.Dash)
            {
                if (note.LinkedTunes != null)
                    foreach (var tune in note.LinkedTunes)
                    {
                        calInfo.PointTime = tune.TargetTime;//添加Tune头部
                        _calInfos.Add(calInfo);
                        calInfo.LaneIndex = tune.TargetLane;
                        calInfo.DoPosAbsolute = !tune.UseRelative;
                        calInfo.Pos = tune.PosValue;
                        calInfo.PointTime += tune.Duration;
                        _calInfos.Add(calInfo);
                    }
                calInfo.PointTime = note.TargetTime + note.SustainDuration;
                _calInfos.Add(calInfo);
            }
            
            Vec2 followPos = GetFollowPos(_calInfos, trackTime);

            foreach (var cal in _calInfos)
            {
                outPoints.Add(trackTime > cal.PointTime ? followPos : CalculatePoint(cal));
            }
        }

        /// <summary>
        /// 计算Tune位置
        /// </summary>
        /// <param name="tune">Tune信息</param>
        /// <param name="note">关联的Dash信息</param>
        /// <param name="trackTime">乐曲时间</param>
        /// <param name="outPoints">输出列表</param>
        public void Calculate(TuneData tune, NoteData note, int trackTime, List<Vec2> outPoints)
        {
            outPoints.Clear();
            
            CalInfo calInfo = new()
            {
                TrackTime = trackTime,
                PointTime = tune.TargetTime,
                LaneIndex = note.LaneIndex,
                Speed = ResolveSpeed(note),
                DoPosAbsolute = !tune.UseRelative,
                Pos = tune.PosValue
            };

            if (note.LinkedTunes != null)
                foreach (var othTune in note.LinkedTunes)
                {
                    if (tune.TargetTime == othTune.TargetTime)//用正解时间来判定是不是同一个Tune
                        break;
                    calInfo.LaneIndex = othTune.TargetLane;
                }
            
            outPoints.Add(CalculatePoint(calInfo));
            
            calInfo.LaneIndex = tune.TargetLane;
            calInfo.DoPosAbsolute = !tune.UseRelative;
            calInfo.Pos = tune.PosValue;
            calInfo.PointTime += tune.Duration;
            
            outPoints.Add(CalculatePoint(calInfo));
        }

        private Vec2 CalculatePoint(CalInfo calInfo)
        {
            Vec2 lanePos = _laneTransform.Position(calInfo.LaneIndex,calInfo.TrackTime);
            Vec2 startPos = calInfo.DoPosAbsolute ? calInfo.Pos : lanePos + calInfo.Pos.Rotate(_laneTransform.Rotation(calInfo.LaneIndex, calInfo.TrackTime));
            float distance = (calInfo.PointTime-calInfo.TrackTime)/1000f*calInfo.Speed;
            Vec2 offset = (new Vec2(0, distance)).Rotate(_laneTransform.Rotation(calInfo.LaneIndex,calInfo.TrackTime));
            Vec2 pointPos = startPos + offset;
            return pointPos;
        }
        
        private Vec2 CalculateStartPos(CalInfo calInfo)
        {
            Vec2 lanePos = _laneTransform.Position(calInfo.LaneIndex,calInfo.TrackTime);
            Vec2 startPos = calInfo.DoPosAbsolute ? calInfo.Pos : lanePos + calInfo.Pos.Rotate(_laneTransform.Rotation(calInfo.LaneIndex, calInfo.TrackTime));
            
            return startPos;
        }

        private Vec2 GetFollowPos(List<CalInfo> calInfos, int trackTime)
        {
            if (calInfos.Count < 2)
                return Vec2.Zero;
            

            int index = 0;
            for (; index < calInfos.Count - 2; index++)
            {
                if (calInfos[index+1].PointTime > trackTime)
                    break;
            }
            
            float present = 1f*(trackTime - calInfos[index].PointTime)/(calInfos[index+1].PointTime - calInfos[index].PointTime);
            
            Vec2 aPos = CalculateStartPos(calInfos[index]);
            Vec2 bPos = CalculateStartPos(calInfos[index+1]);
            
            Vec2 followPos = (bPos -  aPos) *  present +  aPos;
            
            return followPos;
        }

        //计算流速
        private float ResolveSpeed(NoteData note)
        {
            switch (note.SpeedMode)
            {
                case NoteSpeedMode.Default:
                    return GlobalSpeed;
                case NoteSpeedMode.Multiplier:
                    return GlobalSpeed * note.SpeedValue;
                case NoteSpeedMode.Fixed:
                    return note.SpeedValue;
                default:
                    return GlobalSpeed;
            }
        }
    }
}

