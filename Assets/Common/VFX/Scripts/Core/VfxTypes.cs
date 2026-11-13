using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace VFXTest.Vfx
{
    public enum VfxLifecycle
    {
        OneShot = 0,
        Timed = 1,
        Sustained = 2
    }

    public enum VfxChannel
    {
        Background = 0,
        Gameplay = 1,
        Foreground = 2,
        Screen = 3,
        CameraPost = 4
    }

    public enum VfxAttachMode
    {
        WorldFixed = 0,
        FollowPosition = 1,
        FollowFull = 2
    }

    [Flags]
    public enum VfxCapabilities
    {
        None = 0,
        Intensity = 1 << 0,
        Tint = 1 << 1,
        Opacity = 1 << 2,
        TimeScale = 1 << 3
    }

    public enum VfxParamType
    {
        Float = 0,
        Color = 1,
        Bool = 2
    }

    [Serializable]
    public sealed class VfxParamDeclaration
    {
        public string name = "param";
        public VfxParamType type = VfxParamType.Float;
        public float defaultFloat = 1f;
        public float minValue = 0f;
        public float maxValue = 1f;
        public Color defaultColor = Color.white;
        public bool defaultBool;
    }

    [Serializable]
    public struct VfxParamValue
    {
        public string name;
        public VfxParamType type;
        public float floatValue;
        public Color colorValue;
        public bool boolValue;

        public static VfxParamValue MakeFloat(string name, float value)
        {
            return new VfxParamValue { name = name, type = VfxParamType.Float, floatValue = value };
        }

        public static VfxParamValue MakeColor(string name, Color value)
        {
            return new VfxParamValue { name = name, type = VfxParamType.Color, colorValue = value };
        }

        public static VfxParamValue MakeBool(string name, bool value)
        {
            return new VfxParamValue { name = name, type = VfxParamType.Bool, boolValue = value };
        }
    }

    public enum VfxCompareOp
    {
        Equals = 0,
        NotEquals = 1,
        Greater = 2,
        GreaterOrEqual = 3,
        Less = 4,
        LessOrEqual = 5,
        Exists = 6,
        NotExists = 7
    }

    public enum VfxAnchorSource
    {
        SignalPosition = 0,
        SignalOwner = 1
    }

    [Serializable]
    public sealed class VfxRuleCondition
    {
        public string field = "variant";
        public VfxCompareOp op = VfxCompareOp.Equals;
        public string value = "";
    }

    [Serializable]
    public sealed class VfxRuleMapping
    {
        public string source = "intensity";
        public string target = "intensity";
    }

    [Serializable]
    public sealed class VfxRuleEntry
    {
        public string id = "rule";
        public string eventType = "event";
        public string cueId = "";
        public int priority;
        public bool exclusive;
        public VfxAnchorSource anchorSource = VfxAnchorSource.SignalPosition;
        public VfxAttachMode attachMode = VfxAttachMode.WorldFixed;
        public float cooldownOverride = -1f;
        public VfxRuleCondition[] conditions = Array.Empty<VfxRuleCondition>();
        public VfxRuleMapping[] mappings = Array.Empty<VfxRuleMapping>();
    }

    public sealed class VfxSignal
    {
        readonly Dictionary<string, object> context = new Dictionary<string, object>(4);

        public readonly string type;
        public Vector3 position;
        public Transform owner;

        public VfxSignal(string type, Vector3 position, Transform owner = null)
        {
            this.type = type ?? string.Empty;
            this.position = position;
            this.owner = owner;
        }

        public static VfxSignal At(string type, Vector3 position)
        {
            return new VfxSignal(type, position);
        }

        public VfxSignal From(Transform owner)
        {
            this.owner = owner;
            if (owner != null)
            {
                position = owner.position;
            }

            return this;
        }

        public VfxSignal With(string key, object value)
        {
            if (!string.IsNullOrEmpty(key))
            {
                context[key] = value;
            }

            return this;
        }

        public bool TryGet(string key, out object value)
        {
            switch (key)
            {
                case "type":
                    value = type;
                    return true;
                case "position":
                    value = position;
                    return true;
                case "owner":
                    value = owner;
                    return true;
            }

            return context.TryGetValue(key, out value);
        }

        public bool TryGetFloat(string key, out float value)
        {
            value = 0f;
            return TryGet(key, out var raw) && TryConvertFloat(raw, out value);
        }

        public float GetFloat(string key, float fallback)
        {
            return TryGetFloat(key, out var value) ? value : fallback;
        }

        internal static bool TryConvertFloat(object raw, out float value)
        {
            switch (raw)
            {
                case null:
                    value = 0f;
                    return false;
                case float f:
                    value = f;
                    return true;
                case int i:
                    value = i;
                    return true;
                case double d:
                    value = (float)d;
                    return true;
                case long l:
                    value = l;
                    return true;
                case string s:
                    return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
                default:
                    value = 0f;
                    return false;
            }
        }

        internal static bool TryConvertColor(object raw, out Color value)
        {
            if (raw is Color color)
            {
                value = color;
                return true;
            }

            if (raw is string text && !string.IsNullOrEmpty(text))
            {
                if (!text.StartsWith("#"))
                {
                    text = "#" + text;
                }

                return ColorUtility.TryParseHtmlString(text, out value);
            }

            value = default;
            return false;
        }
    }

    public sealed class VfxRequest
    {
        public string cueId;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public Transform follow;
        public VfxAttachMode attachMode = VfxAttachMode.WorldFixed;
        public VfxChannel? channel;
        public int? priority;
        public float intensity = 1f;
        public Color? tint;
        public float opacity = 1f;
        public float timeScale = 1f;
        public List<VfxParamValue> parameters;

        public VfxRequest(string cueId)
        {
            this.cueId = cueId;
        }

        public VfxRequest At(Vector3 worldPosition)
        {
            position = worldPosition;
            return this;
        }

        public VfxRequest Facing(Quaternion worldRotation)
        {
            rotation = worldRotation;
            return this;
        }

        public VfxRequest Follow(Transform target, VfxAttachMode mode = VfxAttachMode.FollowFull)
        {
            follow = target;
            attachMode = target != null ? mode : VfxAttachMode.WorldFixed;
            if (target != null)
            {
                position = target.position;
            }

            return this;
        }

        public VfxRequest InChannel(VfxChannel targetChannel)
        {
            channel = targetChannel;
            return this;
        }

        public VfxRequest WithPriority(int value)
        {
            priority = value;
            return this;
        }

        public VfxRequest WithIntensity(float value)
        {
            intensity = value;
            return this;
        }

        public VfxRequest WithTint(Color value)
        {
            tint = value;
            return this;
        }

        public VfxRequest WithOpacity(float value)
        {
            opacity = value;
            return this;
        }

        public VfxRequest WithTimeScale(float value)
        {
            timeScale = value;
            return this;
        }

        public VfxRequest WithParam(VfxParamValue value)
        {
            parameters ??= new List<VfxParamValue>();
            parameters.Add(value);
            return this;
        }
    }

    public struct VfxStats
    {
        public int active;
        public int pooled;
        public int played;
        public int rejected;
        public int unknownCues;
        public int cooldownSuppressed;
        public int preempted;
    }
}
