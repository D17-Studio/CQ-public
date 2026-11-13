using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CQMusicGame.Shared
{
    /// <summary>
    /// 谱面文本（JSON）与 ChartSheet 之间的序列化适配器
    /// </summary>
    public static class ChartSerializer
    {
        private const int CurrentVersion = 1;

        private static readonly JsonSerializerSettings Settings = new()
        {
            Converters = { new StringEnumConverter { AllowIntegerValues = false } },
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            Formatting = Formatting.Indented,
            Culture = CultureInfo.InvariantCulture,
        };

        /// <summary>
        /// 解析谱面文本；格式或内容错误时抛出 ChartException
        /// </summary>
        /// <param name="text">JSON 谱面文本</param>
        /// <param name="onWarning">非致命问题回调（缺失版本、空谱面等），可为 null</param>
        public static ChartSheet Deserialize(string text, Action<string> onWarning = null)
        {
            if (text == null)
                throw new ChartException("谱面文本为空");

            string json = text.TrimStart((char)0xFEFF);
            if (string.IsNullOrWhiteSpace(json))
                throw new ChartException("谱面文本为空");

            ChartDto dto;
            try
            {
                dto = JsonConvert.DeserializeObject<ChartDto>(json, Settings);
            }
            catch (JsonException ex)
            {
                throw new ChartException($"谱面 JSON 解析失败：{ex.Message}", ex);
            }

            if (dto == null)
                throw new ChartException("谱面内容为空");

            if (dto.Version.HasValue)
            {
                if (dto.Version.Value != CurrentVersion)
                    throw new ChartException($"不支持的谱面版本：{dto.Version.Value}（当前支持 {CurrentVersion}）");
            }
            else
            {
                onWarning?.Invoke("谱面缺少 Version 字段，按版本 1 处理");
            }

            List<NoteData> notes = dto.Notes?.Select(ToNoteData).ToList() ?? new List<NoteData>();
            if (notes.Count == 0)
                onWarning?.Invoke("谱面没有任何 Note");

            LaneEventsDto laneEvents = dto.LaneEvents ?? new LaneEventsDto();

            return new ChartSheet(
                notes,
                laneEvents.Transform?.Select(ToTransform).ToList() ?? new List<LaneEvent.Transform>(),
                laneEvents.Opacity?.Select(ToOpacity).ToList() ?? new List<LaneEvent.Opacity>(),
                laneEvents.Length?.Select(ToLength).ToList() ?? new List<LaneEvent.Length>(),
                laneEvents.Speed?.Select(ToSpeed).ToList() ?? new List<LaneEvent.Speed>(),
                laneEvents.Zone?.Select(ToZone).ToList() ?? new List<LaneEvent.Zone>(),
                laneEvents.Parent?.Select(ToParent).ToList() ?? new List<LaneEvent.Parent>(),
                dto.Bpm?.Select(b => new BpmData { StartTime = b.StartTime, Bpm = b.Bpm }).ToList() ?? new List<BpmData>());
        }

        /// <summary>
        /// 将谱面序列化为 JSON 文本
        /// </summary>
        public static string Serialize(ChartSheet chart)
        {
            if (chart == null)
                throw new ArgumentNullException(nameof(chart));

            var dto = new ChartDto
            {
                Version = CurrentVersion,
                Bpm = chart.GetBpmList().Select(b => new BpmDto { StartTime = b.StartTime, Bpm = b.Bpm }).ToList(),
                Notes = chart.GetNoteList().Select(ToNoteDto).ToList(),
                LaneEvents = new LaneEventsDto
                {
                    Transform = chart.GetTransformEvents().Select(ToTransformDto).ToList(),
                    Opacity = chart.GetOpacityEvents().Select(ToOpacityDto).ToList(),
                    Length = chart.GetLengthEvents().Select(ToLengthDto).ToList(),
                    Speed = chart.GetSpeedEvents().Select(ToSpeedDto).ToList(),
                    Zone = chart.GetZoneEvents().Select(ToZoneDto).ToList(),
                    Parent = chart.GetParentEvents().Select(ToParentDto).ToList(),
                },
            };

            return JsonConvert.SerializeObject(dto, Settings);
        }

        private static NoteData ToNoteData(NoteDto dto)
        {
            TuneData[] tunes = dto.LinkedTunes?.Select(ToTuneData).ToArray();
            return new NoteData
            {
                Type = dto.Type,
                LaneIndex = dto.LaneIndex,
                TargetTime = dto.TargetTime,
                SustainDuration = dto.SustainDuration,
                AppearOffset = dto.AppearOffset,
                Fake = dto.Fake,
                LinkedTunes = tunes,
            };
        }

        private static TuneData ToTuneData(TuneDto dto) =>
            new()
            {
                TargetTime = dto.TargetTime,
                UseRelative = dto.UseRelative,
                TargetLane = dto.TargetLane,
                PosValue = ToVec2(dto.PosValue),
                Duration = dto.Duration,
            };

        private static LaneEvent.Transform ToTransform(TransformDto dto) => new()
        {
            LaneIndex = dto.LaneIndex,
            StartTime = dto.StartTime,
            Duration = dto.Duration,
            Anchor = ToVec2(dto.Anchor),
            XCurve = ToCurve(dto.XCurve),
            YCurve = ToCurve(dto.YCurve),
            RotationCurve = ToCurve(dto.RotationCurve),
            Layer = dto.Layer,
        };

        private static LaneEvent.Opacity ToOpacity(OpacityDto dto) => new()
        {
            LaneIndex = dto.LaneIndex,
            StartTime = dto.StartTime,
            Duration = dto.Duration,
            Targets = dto.Targets,
            ValueCurve = ToCurve(dto.ValueCurve) ?? default,
            Layer = dto.Layer,
        };

        private static LaneEvent.Length ToLength(LengthDto dto) => new()
        {
            LaneIndex = dto.LaneIndex,
            StartTime = dto.StartTime,
            Duration = dto.Duration,
            ValueCurve = ToCurve(dto.ValueCurve) ?? default,
            Layer = dto.Layer,
        };

        private static LaneEvent.Speed ToSpeed(SpeedDto dto) => new()
        {
            LaneIndex = dto.LaneIndex,
            StartTime = dto.StartTime,
            Duration = dto.Duration,
            SpeedMode = dto.SpeedMode,
            ValueCurve = ToCurve(dto.ValueCurve) ?? default,
            Layer = dto.Layer,
        };

        private static LaneEvent.Zone ToZone(ZoneDto dto) => new()
        {
            LaneIndex = dto.LaneIndex,
            StartTime = dto.StartTime,
            TargetZone = dto.TargetZone,
            Layer = dto.Layer,
        };

        private static LaneEvent.Parent ToParent(ParentDto dto) => new()
        {
            LaneIndex = dto.LaneIndex,
            StartTime = dto.StartTime,
            ParentLane = dto.ParentLane,
            Layer = dto.Layer,
        };

        private static ChannelCurve? ToCurve(CurveDto dto) => dto == null
            ? null
            : new ChannelCurve
            {
                From = dto.From,
                FromMode = dto.FromMode,
                To = dto.To,
                ToMode = dto.ToMode,
                Ease = dto.Ease,
            };

        private static Vec2 ToVec2(Vec2Dto dto) => dto == null ? Vec2.Zero : new Vec2(dto.X, dto.Y);

        private static NoteDto ToNoteDto(NoteData note) => new()
        {
            Type = note.Type,
            LaneIndex = note.LaneIndex,
            TargetTime = note.TargetTime,
            SustainDuration = note.SustainDuration,
            AppearOffset = note.AppearOffset,
            Fake = note.Fake,
            LinkedTunes = note.LinkedTunes?.Select(ToTuneDto).ToList(),
        };

        private static TuneDto ToTuneDto(TuneData tune) => new()
        {
            TargetTime = tune.TargetTime,
            UseRelative = tune.UseRelative,
            TargetLane = tune.TargetLane,
            PosValue = ToVec2Dto(tune.PosValue),
            Duration = tune.Duration,
        };

        private static TransformDto ToTransformDto(LaneEvent.Transform e) => new()
        {
            LaneIndex = e.LaneIndex,
            StartTime = e.StartTime,
            Duration = e.Duration,
            Anchor = ToVec2Dto(e.Anchor),
            XCurve = ToCurveDto(e.XCurve),
            YCurve = ToCurveDto(e.YCurve),
            RotationCurve = ToCurveDto(e.RotationCurve),
            Layer = e.Layer,
        };

        private static OpacityDto ToOpacityDto(LaneEvent.Opacity e) => new()
        {
            LaneIndex = e.LaneIndex,
            StartTime = e.StartTime,
            Duration = e.Duration,
            Targets = e.Targets,
            ValueCurve = ToCurveDto(e.ValueCurve),
            Layer = e.Layer,
        };

        private static LengthDto ToLengthDto(LaneEvent.Length e) => new()
        {
            LaneIndex = e.LaneIndex,
            StartTime = e.StartTime,
            Duration = e.Duration,
            ValueCurve = ToCurveDto(e.ValueCurve),
            Layer = e.Layer,
        };

        private static SpeedDto ToSpeedDto(LaneEvent.Speed e) => new()
        {
            LaneIndex = e.LaneIndex,
            StartTime = e.StartTime,
            Duration = e.Duration,
            SpeedMode = e.SpeedMode,
            ValueCurve = ToCurveDto(e.ValueCurve),
            Layer = e.Layer,
        };

        private static ZoneDto ToZoneDto(LaneEvent.Zone e) => new()
        {
            LaneIndex = e.LaneIndex,
            StartTime = e.StartTime,
            TargetZone = e.TargetZone,
            Layer = e.Layer,
        };

        private static ParentDto ToParentDto(LaneEvent.Parent e) => new()
        {
            LaneIndex = e.LaneIndex,
            StartTime = e.StartTime,
            ParentLane = e.ParentLane,
            Layer = e.Layer,
        };

        private static CurveDto ToCurveDto(ChannelCurve? curve) => curve.HasValue
            ? new CurveDto
            {
                From = curve.Value.From,
                FromMode = curve.Value.FromMode,
                To = curve.Value.To,
                ToMode = curve.Value.ToMode,
                Ease = curve.Value.Ease,
            }
            : null;

        private static Vec2Dto ToVec2Dto(Vec2 v) => new() { X = v.x, Y = v.y };
    }

    internal sealed class ChartDto
    {
        public int? Version;
        public List<BpmDto> Bpm;
        public List<NoteDto> Notes;
        public LaneEventsDto LaneEvents;
    }

    internal sealed class LaneEventsDto
    {
        public List<TransformDto> Transform;
        public List<OpacityDto> Opacity;
        public List<LengthDto> Length;
        public List<SpeedDto> Speed;
        public List<ZoneDto> Zone;
        public List<ParentDto> Parent;
    }

    internal sealed class BpmDto
    {
        [JsonRequired] public int StartTime;
        [JsonRequired] public float Bpm;
    }

    internal sealed class NoteDto
    {
        [JsonRequired] public NoteType Type;
        [JsonRequired] public int LaneIndex;
        [JsonRequired] public int TargetTime;
        public int SustainDuration;
        public int AppearOffset = -3000;
        public FakeMode? Fake;
        public List<TuneDto> LinkedTunes;
    }

    internal sealed class TuneDto
    {
        [JsonRequired] public int TargetTime;
        public bool UseRelative;
        [JsonRequired] public int TargetLane;
        [JsonRequired] public Vec2Dto PosValue;
        public int Duration;
    }

    internal sealed class CurveDto
    {
        [JsonRequired] public float From;
        [JsonRequired] public ValueMode FromMode;
        [JsonRequired] public float To;
        [JsonRequired] public ValueMode ToMode;
        [JsonRequired] public EaseCurve Ease;
    }

    internal sealed class Vec2Dto
    {
        [JsonRequired] public float X;
        [JsonRequired] public float Y;
    }

    internal sealed class TransformDto
    {
        [JsonRequired] public int LaneIndex;
        [JsonRequired] public int StartTime;
        public int Duration;
        public Vec2Dto Anchor;
        public CurveDto XCurve;
        public CurveDto YCurve;
        public CurveDto RotationCurve;
        public int Layer;
    }

    internal sealed class OpacityDto
    {
        [JsonRequired] public int LaneIndex;
        [JsonRequired] public int StartTime;
        public int Duration;
        [JsonRequired] public OpacityTarget Targets;
        [JsonRequired] public CurveDto ValueCurve;
        public int Layer;
    }

    internal sealed class LengthDto
    {
        [JsonRequired] public int LaneIndex;
        [JsonRequired] public int StartTime;
        public int Duration;
        [JsonRequired] public CurveDto ValueCurve;
        public int Layer;
    }

    internal sealed class SpeedDto
    {
        [JsonRequired] public int LaneIndex;
        [JsonRequired] public int StartTime;
        public int Duration;
        public LaneSpeedMode SpeedMode;
        [JsonRequired] public CurveDto ValueCurve;
        public int Layer;
    }

    internal sealed class ZoneDto
    {
        [JsonRequired] public int LaneIndex;
        [JsonRequired] public int StartTime;
        [JsonRequired] public JudgeZone TargetZone;
        public int Layer;
    }

    internal sealed class ParentDto
    {
        [JsonRequired] public int LaneIndex;
        [JsonRequired] public int StartTime;
        public int? ParentLane;
        public int Layer;
    }
}
