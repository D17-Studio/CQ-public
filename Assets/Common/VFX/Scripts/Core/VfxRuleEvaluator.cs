using System;
using System.Globalization;
using UnityEngine;

namespace VFXTest.Vfx
{
    public static class VfxRuleEvaluator
    {
        public static bool Matches(VfxRuleEntry rule, VfxSignal signal)
        {
            if (rule == null || signal == null || !string.Equals(rule.eventType, signal.type, StringComparison.Ordinal))
            {
                return false;
            }

            var conditions = rule.conditions;
            if (conditions == null)
            {
                return true;
            }

            for (int i = 0; i < conditions.Length; i++)
            {
                if (!EvaluateCondition(conditions[i], signal))
                {
                    return false;
                }
            }

            return true;
        }

        public static void ApplyTo(VfxRequest request, VfxRuleEntry rule, VfxSignal signal)
        {
            if (request == null || rule == null)
            {
                return;
            }

            if (signal != null)
            {
                bool anchorOnOwner = rule.anchorSource == VfxAnchorSource.SignalOwner && signal.owner != null;
                request.position = anchorOnOwner ? signal.owner.position : signal.position;

                if (rule.attachMode != VfxAttachMode.WorldFixed)
                {
                    request.follow = signal.owner;
                    request.attachMode = signal.owner != null ? rule.attachMode : VfxAttachMode.WorldFixed;
                }
            }

            var mappings = rule.mappings;
            if (mappings == null)
            {
                return;
            }

            for (int i = 0; i < mappings.Length; i++)
            {
                ApplyMapping(request, signal, mappings[i]);
            }
        }

        static void ApplyMapping(VfxRequest request, VfxSignal signal, VfxRuleMapping mapping)
        {
            if (mapping == null || string.IsNullOrEmpty(mapping.target) || signal == null)
            {
                return;
            }

            if (!signal.TryGet(mapping.source, out var raw) || raw == null)
            {
                return;
            }

            switch (mapping.target.ToLowerInvariant())
            {
                case "intensity":
                    if (VfxSignal.TryConvertFloat(raw, out var intensity))
                    {
                        request.intensity = intensity;
                    }

                    break;
                case "opacity":
                    if (VfxSignal.TryConvertFloat(raw, out var opacity))
                    {
                        request.opacity = opacity;
                    }

                    break;
                case "timescale":
                    if (VfxSignal.TryConvertFloat(raw, out var timeScale))
                    {
                        request.timeScale = timeScale;
                    }

                    break;
                case "priority":
                    if (VfxSignal.TryConvertFloat(raw, out var priority))
                    {
                        request.priority = Mathf.RoundToInt(priority);
                    }

                    break;
                case "tint":
                case "color":
                    if (VfxSignal.TryConvertColor(raw, out var color))
                    {
                        request.tint = color;
                    }

                    break;
                default:
                    if (mapping.target.StartsWith("param:", StringComparison.OrdinalIgnoreCase))
                    {
                        var paramName = mapping.target.Substring("param:".Length);
                        ApplyParamMapping(request, paramName, raw);
                    }

                    break;
            }
        }

        static void ApplyParamMapping(VfxRequest request, string paramName, object raw)
        {
            if (string.IsNullOrEmpty(paramName))
            {
                return;
            }

            if (raw is Color color)
            {
                request.WithParam(VfxParamValue.MakeColor(paramName, color));
            }
            else if (raw is bool flag)
            {
                request.WithParam(VfxParamValue.MakeBool(paramName, flag));
            }
            else if (VfxSignal.TryConvertFloat(raw, out var number))
            {
                request.WithParam(VfxParamValue.MakeFloat(paramName, number));
            }
        }

        static bool EvaluateCondition(VfxRuleCondition condition, VfxSignal signal)
        {
            if (condition == null || string.IsNullOrEmpty(condition.field))
            {
                return true;
            }

            bool found = signal.TryGet(condition.field, out var actual);
            switch (condition.op)
            {
                case VfxCompareOp.Exists:
                    return found && actual != null;
                case VfxCompareOp.NotExists:
                    return !found || actual == null;
            }

            if (!found || actual == null)
            {
                return false;
            }

            switch (condition.op)
            {
                case VfxCompareOp.Equals:
                    return ValuesEqual(actual, condition.value);
                case VfxCompareOp.NotEquals:
                    return !ValuesEqual(actual, condition.value);
                case VfxCompareOp.Greater:
                    return CompareNumber(actual, condition.value) > 0;
                case VfxCompareOp.GreaterOrEqual:
                    return CompareNumber(actual, condition.value) >= 0;
                case VfxCompareOp.Less:
                    return CompareNumber(actual, condition.value) < 0;
                case VfxCompareOp.LessOrEqual:
                    return CompareNumber(actual, condition.value) <= 0;
                default:
                    return false;
            }
        }

        static bool ValuesEqual(object actual, string expected)
        {
            if (actual is bool flag)
            {
                return bool.TryParse(expected, out var parsed) && parsed == flag;
            }

            if (actual is string text)
            {
                return string.Equals(text, expected, StringComparison.OrdinalIgnoreCase);
            }

            if (VfxSignal.TryConvertFloat(actual, out var actualNumber) &&
                float.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var expectedNumber))
            {
                return Mathf.Approximately(actualNumber, expectedNumber);
            }

            return string.Equals(actual.ToString(), expected, StringComparison.OrdinalIgnoreCase);
        }

        static int CompareNumber(object actual, string expected)
        {
            if (!VfxSignal.TryConvertFloat(actual, out var actualNumber) ||
                !float.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var expectedNumber))
            {
                return 0;
            }

            return actualNumber.CompareTo(expectedNumber);
        }
    }
}
