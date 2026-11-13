using System;
using System.Collections.Generic;

namespace CQMusicGame.Shared
{
    /// <summary>
    /// 计算Note的位置属性
    /// </summary>
    public class NotePositionCalculator
    {
        private readonly float _globalSpeed;
        private readonly LaneState _laneState;

        private readonly List<CalInfo> _calInfos = new();

        public NotePositionCalculator(float globalSpeed, LaneState laneState)
        {
            _globalSpeed = globalSpeed;
            _laneState = laneState;
        }

        private struct CalInfo
        {
            public int TrackTime;
            public int PointTime;
            public int LaneIndex;
            public int SpeedLaneIndex;
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
                SpeedLaneIndex = note.LaneIndex,
                DoPosAbsolute = false,
                Pos = Vec2.Zero
            };

            _calInfos.Add(calInfo);

            if (note.Type == NoteType.Dot)
            {
                outPoints.Add(CalculatePoint(calInfo));
                return;
            }

            if (note.Type == NoteType.Dash && note.LinkedTunes != null)
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

            int endTime = note.TargetTime + note.SustainDuration;
            // 最后一个Tune在结束点收尾时不再追加尾点，避免出现两个相同时间的点
            if (_calInfos.Count < 2 || endTime > _calInfos[^1].PointTime)
            {
                calInfo.PointTime = endTime;
                _calInfos.Add(calInfo);
            }

            Vec2 followPos = GetFollowPos(_calInfos, trackTime);

            foreach (var cal in _calInfos)
            {
                outPoints.Add(trackTime > cal.PointTime ? followPos : CalculatePoint(cal));
            }
        }

        private Vec2 CalculatePoint(CalInfo calInfo)
        {
            LaneFrame frame = _laneState.GetFrame(calInfo.LaneIndex, calInfo.TrackTime);
            Vec2 startPos = calInfo.DoPosAbsolute ? calInfo.Pos : frame.Position + calInfo.Pos.Rotate(frame.Rotation);
            float distance = _laneState.IntegrateSpeed(calInfo.SpeedLaneIndex, calInfo.TrackTime, calInfo.PointTime, _globalSpeed);
            Vec2 offset = (new Vec2(0, distance)).Rotate(frame.Rotation);
            return startPos + offset;
        }

        private Vec2 CalculateStartPos(CalInfo calInfo)
        {
            LaneFrame frame = _laneState.GetFrame(calInfo.LaneIndex, calInfo.TrackTime);
            return calInfo.DoPosAbsolute ? calInfo.Pos : frame.Position + calInfo.Pos.Rotate(frame.Rotation);
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

            int span = calInfos[index+1].PointTime - calInfos[index].PointTime;
            float present = span <= 0
                ? 1f
                : Math.Min(Math.Max(1f * (trackTime - calInfos[index].PointTime) / span, 0f), 1f);

            Vec2 aPos = CalculateStartPos(calInfos[index]);
            Vec2 bPos = CalculateStartPos(calInfos[index+1]);

            Vec2 followPos = (bPos -  aPos) *  present +  aPos;

            return followPos;
        }

    }
}
