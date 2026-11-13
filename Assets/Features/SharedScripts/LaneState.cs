using System;
using System.Collections.Generic;

namespace CQMusicGame.Shared
{
    /// <summary> 轨道帧：世界坐标位置与旋转 </summary>
    public readonly struct LaneFrame
    {
        public readonly Vec2 Position;
        public readonly float Rotation;

        public LaneFrame(Vec2 position, float rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }

    /// <summary> 轨道流速采样结果 </summary>
    public readonly struct LaneSpeedSample
    {
        public readonly LaneSpeedMode Mode;
        public readonly float Value;

        public LaneSpeedSample(LaneSpeedMode mode, float value)
        {
            Mode = mode;
            Value = value;
        }
    }

    /// <summary>
    /// 轨道运行时状态：按通道存储曲线，提供位置/旋转/透明度/长度/流速/判定区/父轨查询
    /// </summary>
    public sealed class LaneState
    {
        private const float HeadOpacityFallback = 0f;
        private const float LineOpacityFallback = 0f;
        private const float LabelOpacityFallback = 0f;
        private const float NoteOpacityFallback = 1f;
        private const float LengthFallback = 1f;
        private const float SpeedFallback = 1f;

        private readonly Dictionary<int, LaneChannels> _lanes = new();
        private readonly Dictionary<int, LaneSpeedIntegral> _speedIntegrals = new();

        /// <summary> 由六类轨道事件构建运行时状态 </summary>
        public LaneState(
            IReadOnlyList<LaneEvent.Transform> transforms,
            IReadOnlyList<LaneEvent.Opacity> opacities,
            IReadOnlyList<LaneEvent.Length> lengths,
            IReadOnlyList<LaneEvent.Speed> speeds,
            IReadOnlyList<LaneEvent.Zone> zones,
            IReadOnlyList<LaneEvent.Parent> parents)
        {
            BuildTransforms(transforms);
            BuildOpacities(opacities);
            BuildLengths(lengths);
            BuildSpeeds(speeds);
            BuildSpeedIntegrals();
            BuildZones(zones);
            BuildParents(parents);
        }

        /// <summary> 查询轨道帧（父轨合成后的世界坐标与旋转） </summary>
        public LaneFrame GetFrame(int laneIndex, int time)
        {
            LaneFrame local = GetLocalFrame(laneIndex, time);
            int? parentLane = Parent(laneIndex, time);
            if (!parentLane.HasValue)
                return local;

            var visited = new HashSet<int> { laneIndex };
            if (!visited.Add(parentLane.Value))
                return local;

            return ComposeWithParent(local, parentLane.Value, time, visited);
        }

        /// <summary> 查询轨道头不透明度 </summary>
        public float HeadOpacity(int laneIndex, int time)
            => _lanes.TryGetValue(laneIndex, out LaneChannels lane)
                ? EvaluateChannel(lane.HeadOpacity, time, HeadOpacityFallback)
                : HeadOpacityFallback;

        /// <summary> 查询轨道线不透明度 </summary>
        public float LineOpacity(int laneIndex, int time)
            => _lanes.TryGetValue(laneIndex, out LaneChannels lane)
                ? EvaluateChannel(lane.LineOpacity, time, LineOpacityFallback)
                : LineOpacityFallback;

        /// <summary> 查询轨道标签不透明度 </summary>
        public float LabelOpacity(int laneIndex, int time)
            => _lanes.TryGetValue(laneIndex, out LaneChannels lane)
                ? EvaluateChannel(lane.LabelOpacity, time, LabelOpacityFallback)
                : LabelOpacityFallback;

        /// <summary> 查询轨道上 Note 的不透明度 </summary>
        public float NoteOpacity(int laneIndex, int time)
            => _lanes.TryGetValue(laneIndex, out LaneChannels lane)
                ? EvaluateChannel(lane.NoteOpacity, time, NoteOpacityFallback)
                : NoteOpacityFallback;

        /// <summary> 查询轨道线长度倍率 </summary>
        public float Length(int laneIndex, int time)
            => _lanes.TryGetValue(laneIndex, out LaneChannels lane)
                ? EvaluateChannel(lane.Length, time, LengthFallback)
                : LengthFallback;

        /// <summary> 查询轨道流速采样 </summary>
        public LaneSpeedSample Speed(int laneIndex, int time)
        {
            if (!_lanes.TryGetValue(laneIndex, out LaneChannels lane) ||
                !TryFindActiveSpeed(lane.Speed, time, out LaneSpeedEntry entry))
                return new LaneSpeedSample(LaneSpeedMode.Multiplier, SpeedFallback);

            return new LaneSpeedSample(entry.Mode, EvaluateCurve(entry.Curve, time));
        }

        /// <summary>
        /// 查询轨道流速在 [fromTime, toTime] 上的积分距离；Multiplier 段按 globalSpeed 缩放
        /// </summary>
        public float IntegrateSpeed(int laneIndex, int fromTime, int toTime, float globalSpeed)
        {
            if (fromTime == toTime)
                return 0f;

            if (fromTime > toTime)
                return -IntegrateSpeed(laneIndex, toTime, fromTime, globalSpeed);

            if (!_speedIntegrals.TryGetValue(laneIndex, out LaneSpeedIntegral integral))
                return globalSpeed * (toTime - fromTime) / 1000f;

            integral.GetCumulative(toTime, out float multiplierTo, out float fixedTo);
            integral.GetCumulative(fromTime, out float multiplierFrom, out float fixedFrom);

            return ((multiplierTo - multiplierFrom) * globalSpeed + fixedTo - fixedFrom) / 1000f;
        }

        /// <summary> 查询指定时刻生效的判定区 </summary>
        public JudgeZone Zone(int laneIndex, int time)
        {
            if (!_lanes.TryGetValue(laneIndex, out LaneChannels lane))
                return JudgeZone.All;

            for (int i = lane.Zones.Count - 1; i >= 0; i--)
            {
                if (lane.Zones[i].StartTime <= time)
                    return lane.Zones[i].TargetZone;
            }

            return JudgeZone.All;
        }

        /// <summary> 查询指定时刻的父轨（null = 无父） </summary>
        public int? Parent(int laneIndex, int time)
        {
            if (!_lanes.TryGetValue(laneIndex, out LaneChannels lane))
                return null;

            for (int i = lane.Parents.Count - 1; i >= 0; i--)
            {
                if (lane.Parents[i].StartTime <= time)
                    return lane.Parents[i].ParentLane;
            }

            return null;
        }

        #region 构建

        private void BuildTransforms(IReadOnlyList<LaneEvent.Transform> transforms)
        {
            foreach (LaneEvent.Transform e in transforms)
            {
                LaneChannels lane = GetOrCreateLane(e.LaneIndex);

                if (e.RotationCurve is { } rotationCurve)
                {
                    float startValue = rotationCurve.FromMode == ValueMode.Relative
                        ? EvaluateChannel(lane.Rotation, e.StartTime, 0f) + rotationCurve.From
                        : rotationCurve.From;

                    LaneChannelEntry rotationEntry = MakeEntry(e.StartTime, e.Duration, rotationCurve, startValue);
                    // 锚点挂在旋转 entry 上：求值时按"当前生效的旋转"取锚点，
                    // 纯旋转事件、以及 X/Y 与旋转分属不同事件时也能正确绕锚点旋转
                    rotationEntry.Anchor = e.Anchor;
                    AddEntry(lane.Rotation, rotationEntry);
                }

                if (e.XCurve is { } xCurve)
                {
                    // 绝对模式：值即轨道头坐标；相对模式：从当前实际头位置（含已有锚点位移）起算
                    float startValue = xCurve.FromMode == ValueMode.Relative
                        ? EvaluatePositionChannel(lane, lane.X, e.StartTime, true) + xCurve.From
                        : xCurve.From;

                    AddEntry(lane.X, MakeEntry(e.StartTime, e.Duration, xCurve, startValue));
                }

                if (e.YCurve is { } yCurve)
                {
                    float startValue = yCurve.FromMode == ValueMode.Relative
                        ? EvaluatePositionChannel(lane, lane.Y, e.StartTime, false) + yCurve.From
                        : yCurve.From;

                    AddEntry(lane.Y, MakeEntry(e.StartTime, e.Duration, yCurve, startValue));
                }
            }
        }

        private void BuildOpacities(IReadOnlyList<LaneEvent.Opacity> opacities)
        {
            foreach (LaneEvent.Opacity e in opacities)
            {
                LaneChannels lane = GetOrCreateLane(e.LaneIndex);

                if ((e.Targets & OpacityTarget.Head) != 0)
                    AddScalarEntry(lane.HeadOpacity, e.StartTime, e.Duration, e.ValueCurve, HeadOpacityFallback);
                if ((e.Targets & OpacityTarget.Line) != 0)
                    AddScalarEntry(lane.LineOpacity, e.StartTime, e.Duration, e.ValueCurve, LineOpacityFallback);
                if ((e.Targets & OpacityTarget.Label) != 0)
                    AddScalarEntry(lane.LabelOpacity, e.StartTime, e.Duration, e.ValueCurve, LabelOpacityFallback);
                if ((e.Targets & OpacityTarget.Note) != 0)
                    AddScalarEntry(lane.NoteOpacity, e.StartTime, e.Duration, e.ValueCurve, NoteOpacityFallback);
            }
        }

        private void BuildLengths(IReadOnlyList<LaneEvent.Length> lengths)
        {
            foreach (LaneEvent.Length e in lengths)
            {
                LaneChannels lane = GetOrCreateLane(e.LaneIndex);
                AddScalarEntry(lane.Length, e.StartTime, e.Duration, e.ValueCurve, LengthFallback);
            }
        }

        private void BuildSpeeds(IReadOnlyList<LaneEvent.Speed> speeds)
        {
            foreach (LaneEvent.Speed e in speeds)
            {
                LaneChannels lane = GetOrCreateLane(e.LaneIndex);

                float startValue = e.ValueCurve.FromMode == ValueMode.Relative
                    ? EvaluateSpeedChannel(lane.Speed, e.StartTime, SpeedFallback) + e.ValueCurve.From
                    : e.ValueCurve.From;

                LaneChannelEntry entry = MakeEntry(e.StartTime, e.Duration, e.ValueCurve, startValue);
                entry.DoPersistEnd = !IsSpeedEndCovered(lane.Speed, entry.StartTime, entry.Duration);
                lane.Speed.Add(new LaneSpeedEntry { Curve = entry, Mode = e.SpeedMode });
            }
        }

        private void BuildSpeedIntegrals()
        {
            foreach (KeyValuePair<int, LaneChannels> pair in _lanes)
            {
                if (pair.Value.Speed.Count == 0)
                    continue;

                _speedIntegrals.Add(pair.Key, new LaneSpeedIntegral(pair.Value.Speed));
            }
        }

        private void BuildZones(IReadOnlyList<LaneEvent.Zone> zones)
        {
            foreach (LaneEvent.Zone e in zones)
                GetOrCreateLane(e.LaneIndex).Zones.Add(e);
        }

        private void BuildParents(IReadOnlyList<LaneEvent.Parent> parents)
        {
            foreach (LaneEvent.Parent e in parents)
                GetOrCreateLane(e.LaneIndex).Parents.Add(e);
        }

        private static void AddScalarEntry(List<LaneChannelEntry> channel, int startTime, int duration, in ChannelCurve curve, float fallback)
        {
            float startValue = curve.FromMode == ValueMode.Relative
                ? EvaluateChannel(channel, startTime, fallback) + curve.From
                : curve.From;

            AddEntry(channel, MakeEntry(startTime, duration, curve, startValue));
        }

        private static LaneChannelEntry MakeEntry(int startTime, int duration, in ChannelCurve curve, float startValue)
        {
            return new LaneChannelEntry
            {
                StartTime = startTime,
                Duration = duration,
                StartValue = startValue,
                EndValue = curve.ToMode == ValueMode.Relative ? startValue + curve.To : curve.To,
                Ease = curve.Ease
            };
        }

        private static void AddEntry(List<LaneChannelEntry> channel, LaneChannelEntry entry)
        {
            entry.DoPersistEnd = !IsEndCovered(channel, entry.StartTime, entry.Duration);
            channel.Add(entry);
        }

        private static bool IsEndCovered(List<LaneChannelEntry> channel, int startTime, int duration)
        {
            int end = startTime + duration;
            foreach (LaneChannelEntry existing in channel)
            {
                if (existing.StartTime <= end && existing.StartTime + existing.Duration > end)
                    return true;
            }
            return false;
        }

        private static bool IsSpeedEndCovered(List<LaneSpeedEntry> channel, int startTime, int duration)
        {
            int end = startTime + duration;
            foreach (LaneSpeedEntry existing in channel)
            {
                if (existing.Curve.StartTime <= end && existing.Curve.StartTime + existing.Curve.Duration > end)
                    return true;
            }
            return false;
        }

        private LaneChannels GetOrCreateLane(int laneIndex)
        {
            if (!_lanes.TryGetValue(laneIndex, out LaneChannels lane))
            {
                lane = new LaneChannels();
                _lanes.Add(laneIndex, lane);
            }
            return lane;
        }

        #endregion

        #region 求值

        private LaneFrame GetLocalFrame(int laneIndex, int time)
        {
            if (!_lanes.TryGetValue(laneIndex, out LaneChannels lane))
                return new LaneFrame(Vec2.Zero, 0f);

            float x = EvaluatePositionChannel(lane, lane.X, time, true);
            float y = EvaluatePositionChannel(lane, lane.Y, time, false);
            float rotation = EvaluateChannel(lane.Rotation, time, 0f);
            return new LaneFrame(new Vec2(x, y), rotation);
        }

        private LaneFrame ComposeWithParent(LaneFrame child, int parentLane, int time, HashSet<int> visited)
        {
            LaneFrame parentLocal = GetLocalFrame(parentLane, time);
            int? grandParent = Parent(parentLane, time);
            if (grandParent.HasValue && visited.Add(grandParent.Value))
                parentLocal = ComposeWithParent(parentLocal, grandParent.Value, time, visited);

            Vec2 position = parentLocal.Position + child.Position.Rotate(parentLocal.Rotation);
            return new LaneFrame(position, parentLocal.Rotation + child.Rotation);
        }

        /// <summary>
        /// 求位置通道：X/Y 曲线值正常情况下就是轨道头坐标；
        /// 锚点只在所属旋转段内作为辅助量参与（段内偏移 = R(θ_段起点)·A − R(θ(t))·A）。
        /// 位置值一旦由新的位置 entry 重新书写（新建曲线/绝对节点），之前的锚点偏移即作废，
        /// 因为新写入的值本身已经代表轨道头的实际坐标。
        /// </summary>
        private static float EvaluatePositionChannel(LaneChannels lane, List<LaneChannelEntry> channel, int time, bool isX)
        {
            float value = EvaluateChannel(channel, time, 0f);

            int posIndex = FindActiveIndex(channel, time);
            int posStartTime = posIndex >= 0 ? channel[posIndex].StartTime : int.MinValue;

            Vec2 offset = EvaluateAnchorOffset(lane, posStartTime, time);
            return isX ? value + offset.x : value + offset.y;
        }

        /// <summary>
        /// 累计锚点补偿：只统计"当前位置 entry 起点之后"启动的旋转段；
        /// 已被后续旋转段接替的段在接替点冻结，当前段按 time 动态计算（结束后自动钳制为常量）。
        /// </summary>
        private static Vec2 EvaluateAnchorOffset(LaneChannels lane, int posStartTime, int time)
        {
            Vec2 offset = Vec2.Zero;
            List<LaneChannelEntry> rotations = lane.Rotation;

            for (int i = 0; i < rotations.Count; i++)
            {
                LaneChannelEntry rotation = rotations[i];
                if (rotation.StartTime > time)
                    break;
                if (rotation.StartTime < posStartTime)
                    continue;

                int freezeTime = i + 1 < rotations.Count && rotations[i + 1].StartTime <= time
                    ? rotations[i + 1].StartTime
                    : time;

                offset += SegmentAnchorOffset(rotation, freezeTime);
            }

            return offset;
        }

        /// <summary> 旋转段从自身起点到 time 的锚点补偿：R(θ_段起点)·A − R(θ(time))·A </summary>
        private static Vec2 SegmentAnchorOffset(in LaneChannelEntry rotation, int time)
        {
            Vec2 startAnchor = rotation.Anchor.Rotate(rotation.StartValue);
            Vec2 currentAnchor = rotation.Anchor.Rotate(EvaluateCurve(rotation, time));
            return startAnchor - currentAnchor;
        }

        private static float EvaluateChannel(List<LaneChannelEntry> channel, int time, float fallback)
            => TryFindActive(channel, time, out LaneChannelEntry entry)
                ? EvaluateCurve(entry, time)
                : fallback;

        private static float EvaluateSpeedChannel(List<LaneSpeedEntry> channel, int time, float fallback)
            => TryFindActiveSpeed(channel, time, out LaneSpeedEntry entry)
                ? EvaluateCurve(entry.Curve, time)
                : fallback;

        /// <summary> 返回当前生效 entry 的下标（-1 = 无），用于取对齐的累计偏移 </summary>
        private static int FindActiveIndex(List<LaneChannelEntry> channel, int time)
        {
            for (int i = channel.Count - 1; i >= 0; i--)
            {
                LaneChannelEntry candidate = channel[i];
                if (candidate.StartTime > time)
                    continue;

                if (time <= candidate.StartTime + candidate.Duration || candidate.DoPersistEnd)
                    return i;
            }

            return -1;
        }

        private static bool TryFindActive(List<LaneChannelEntry> channel, int time, out LaneChannelEntry entry)
        {
            for (int i = channel.Count - 1; i >= 0; i--)
            {
                LaneChannelEntry candidate = channel[i];
                if (candidate.StartTime > time)
                    continue;

                if (time <= candidate.StartTime + candidate.Duration || candidate.DoPersistEnd)
                {
                    entry = candidate;
                    return true;
                }
            }

            entry = default;
            return false;
        }

        private static bool TryFindActiveSpeed(List<LaneSpeedEntry> channel, int time, out LaneSpeedEntry entry)
        {
            for (int i = channel.Count - 1; i >= 0; i--)
            {
                LaneSpeedEntry candidate = channel[i];
                if (candidate.Curve.StartTime > time)
                    continue;

                if (time <= candidate.Curve.StartTime + candidate.Curve.Duration || candidate.Curve.DoPersistEnd)
                {
                    entry = candidate;
                    return true;
                }
            }

            entry = default;
            return false;
        }

        private static float EvaluateCurve(in LaneChannelEntry entry, int time)
        {
            if (time <= entry.StartTime)
                return entry.StartValue;
            if (time >= entry.StartTime + entry.Duration)
                return entry.EndValue;

            float t = (time - entry.StartTime) / (float)entry.Duration;
            return entry.StartValue + (entry.EndValue - entry.StartValue) * Easing.Apply(entry.Ease, t);
        }

        #endregion

        /// <summary> 单条轨道的流速积分表：断点 + 分段生效曲线 + 分段前缀积分 </summary>
        private sealed class LaneSpeedIntegral
        {
            private readonly int[] _bounds;
            private readonly Segment[] _segments;
            private readonly float[] _cumulativeMultiplier;
            private readonly float[] _cumulativeFixed;
            private readonly bool _hasTail;
            private readonly LaneSpeedEntry _tail;

            public LaneSpeedIntegral(List<LaneSpeedEntry> entries)
            {
                var bounds = new List<int>(entries.Count * 2);
                foreach (LaneSpeedEntry entry in entries)
                {
                    bounds.Add(entry.Curve.StartTime);
                    bounds.Add(entry.Curve.StartTime + entry.Curve.Duration);
                }
                bounds.Sort();

                var unique = new List<int>(bounds.Count);
                foreach (int bound in bounds)
                {
                    if (unique.Count == 0 || unique[^1] != bound)
                        unique.Add(bound);
                }

                _bounds = unique.ToArray();

                int segmentCount = _bounds.Length - 1;
                _segments = new Segment[segmentCount];
                _cumulativeMultiplier = new float[segmentCount + 1];
                _cumulativeFixed = new float[segmentCount + 1];

                for (int i = 0; i < segmentCount; i++)
                {
                    Segment segment = ResolveSegment(entries, _bounds[i], _bounds[i + 1]);
                    _segments[i] = segment;

                    float raw = segment.Fallback
                        ? _bounds[i + 1] - _bounds[i]
                        : IntegrateEntry(segment.Entry.Curve, _bounds[i], _bounds[i + 1]);

                    _cumulativeMultiplier[i + 1] = _cumulativeMultiplier[i] +
                        (segment.Fallback || segment.Entry.Mode == LaneSpeedMode.Multiplier ? raw : 0f);
                    _cumulativeFixed[i + 1] = _cumulativeFixed[i] +
                        (!segment.Fallback && segment.Entry.Mode == LaneSpeedMode.Fixed ? raw : 0f);
                }

                _tail = ResolveTail(entries, _bounds[^1], out _hasTail);
            }

            public void GetCumulative(int time, out float multiplier, out float fixedValue)
            {
                int lastIndex = _bounds.Length - 1;

                if (time <= _bounds[0])
                {
                    multiplier = time - _bounds[0];
                    fixedValue = 0f;
                    return;
                }

                if (time >= _bounds[lastIndex])
                {
                    multiplier = _cumulativeMultiplier[lastIndex];
                    fixedValue = _cumulativeFixed[lastIndex];

                    if (_hasTail)
                    {
                        float raw = _tail.Curve.EndValue * (time - _bounds[lastIndex]);
                        if (_tail.Mode == LaneSpeedMode.Multiplier)
                            multiplier += raw;
                        else
                            fixedValue += raw;
                    }
                    else
                    {
                        multiplier += time - _bounds[lastIndex];
                    }

                    return;
                }

                int index = Array.BinarySearch(_bounds, time);
                if (index >= 0)
                {
                    multiplier = _cumulativeMultiplier[index];
                    fixedValue = _cumulativeFixed[index];
                    return;
                }

                index = ~index - 1;
                Segment segment = _segments[index];
                float partial = segment.Fallback
                    ? time - _bounds[index]
                    : IntegrateEntry(segment.Entry.Curve, _bounds[index], time);

                multiplier = _cumulativeMultiplier[index];
                fixedValue = _cumulativeFixed[index];

                if (segment.Fallback || segment.Entry.Mode == LaneSpeedMode.Multiplier)
                    multiplier += partial;
                else
                    fixedValue += partial;
            }

            private static Segment ResolveSegment(List<LaneSpeedEntry> entries, int start, int end)
            {
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    LaneSpeedEntry entry = entries[i];
                    if (entry.Curve.StartTime > start)
                        continue;

                    if (entry.Curve.StartTime + entry.Curve.Duration >= end || entry.Curve.DoPersistEnd)
                        return new Segment { Entry = entry, Fallback = false };
                }

                return new Segment { Fallback = true };
            }

            private static LaneSpeedEntry ResolveTail(List<LaneSpeedEntry> entries, int lastBound, out bool hasTail)
            {
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    LaneSpeedEntry entry = entries[i];
                    if (entry.Curve.StartTime <= lastBound && entry.Curve.DoPersistEnd)
                    {
                        hasTail = true;
                        return entry;
                    }
                }

                hasTail = false;
                return default;
            }

            private static float IntegrateEntry(in LaneChannelEntry entry, int fromTime, int toTime)
            {
                if (fromTime >= toTime)
                    return 0f;

                int entryStart = entry.StartTime;
                int entryEnd = entry.StartTime + entry.Duration;
                float result = 0f;

                if (fromTime < entryStart)
                {
                    int end = Math.Min(toTime, entryStart);
                    result += entry.StartValue * (end - fromTime);
                    fromTime = end;
                }

                if (toTime > entryEnd)
                {
                    int start = Math.Max(fromTime, entryEnd);
                    result += entry.EndValue * (toTime - start);
                    toTime = start;
                }

                if (fromTime < toTime && entry.Duration > 0)
                {
                    float ua = (fromTime - entryStart) / (float)entry.Duration;
                    float ub = (toTime - entryStart) / (float)entry.Duration;
                    result += entry.StartValue * (toTime - fromTime)
                        + (entry.EndValue - entry.StartValue) * entry.Duration
                        * (Easing.Integral(entry.Ease, ub) - Easing.Integral(entry.Ease, ua));
                }

                return result;
            }

            private struct Segment
            {
                public LaneSpeedEntry Entry;
                public bool Fallback;
            }
        }

        private sealed class LaneChannels
        {
            public readonly List<LaneChannelEntry> X = new();
            public readonly List<LaneChannelEntry> Y = new();
            public readonly List<LaneChannelEntry> Rotation = new();
            public readonly List<LaneChannelEntry> HeadOpacity = new();
            public readonly List<LaneChannelEntry> LineOpacity = new();
            public readonly List<LaneChannelEntry> LabelOpacity = new();
            public readonly List<LaneChannelEntry> NoteOpacity = new();
            public readonly List<LaneChannelEntry> Length = new();
            public readonly List<LaneSpeedEntry> Speed = new();
            public readonly List<LaneEvent.Zone> Zones = new();
            public readonly List<LaneEvent.Parent> Parents = new();
        }

        private struct LaneChannelEntry
        {
            public int StartTime;
            public int Duration;
            public float StartValue;
            public float EndValue;
            public EaseCurve Ease;
            public bool DoPersistEnd;

            // 仅旋转 entry 使用：该旋转事件的世界锚点局部偏移
            public Vec2 Anchor;
        }

        private struct LaneSpeedEntry
        {
            public LaneChannelEntry Curve;
            public LaneSpeedMode Mode;
        }
    }
}
