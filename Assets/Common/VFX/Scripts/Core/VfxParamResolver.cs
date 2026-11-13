using System;
using System.Collections.Generic;
using UnityEngine;

namespace VFXTest.Vfx
{
    public interface IVfxParameterSink
    {
        void ApplyParameters(VfxParamValues values);
    }

    public sealed class VfxParamValues
    {
        readonly Dictionary<string, VfxParamValue> values;

        internal VfxParamValues(int capacity)
        {
            values = new Dictionary<string, VfxParamValue>(capacity, StringComparer.Ordinal);
        }

        public bool TryGet(string name, out VfxParamValue value)
        {
            return values.TryGetValue(name, out value);
        }

        public float GetFloat(string name, float fallback = 0f)
        {
            return values.TryGetValue(name, out var value) && value.type == VfxParamType.Float
                ? value.floatValue
                : fallback;
        }

        public Color GetColor(string name, Color? fallback = null)
        {
            return values.TryGetValue(name, out var value) && value.type == VfxParamType.Color
                ? value.colorValue
                : fallback ?? Color.white;
        }

        public bool GetBool(string name, bool fallback = false)
        {
            return values.TryGetValue(name, out var value) && value.type == VfxParamType.Bool
                ? value.boolValue
                : fallback;
        }

        internal void Set(VfxParamValue value)
        {
            if (!string.IsNullOrEmpty(value.name))
            {
                values[value.name] = value;
            }
        }
    }

    public static class VfxParamResolver
    {
        public static VfxParamValues Resolve(VfxParamDeclaration[] declarations, IList<VfxParamValue> overrides)
        {
            var result = new VfxParamValues(declarations?.Length ?? 0);
            if (declarations != null)
            {
                for (int i = 0; i < declarations.Length; i++)
                {
                    var declaration = declarations[i];
                    if (declaration == null || string.IsNullOrEmpty(declaration.name))
                    {
                        continue;
                    }

                    switch (declaration.type)
                    {
                        case VfxParamType.Float:
                            result.Set(VfxParamValue.MakeFloat(declaration.name,
                                Mathf.Clamp(declaration.defaultFloat, Minimum(declaration), Maximum(declaration))));
                            break;
                        case VfxParamType.Color:
                            result.Set(VfxParamValue.MakeColor(declaration.name, declaration.defaultColor));
                            break;
                        case VfxParamType.Bool:
                            result.Set(VfxParamValue.MakeBool(declaration.name, declaration.defaultBool));
                            break;
                    }
                }
            }

            if (overrides != null)
            {
                for (int i = 0; i < overrides.Count; i++)
                {
                    var candidate = overrides[i];
                    var declaration = FindDeclaration(declarations, candidate.name);
                    if (declaration == null || declaration.type != candidate.type)
                    {
                        continue;
                    }

                    switch (candidate.type)
                    {
                        case VfxParamType.Float:
                            result.Set(VfxParamValue.MakeFloat(candidate.name,
                                Mathf.Clamp(candidate.floatValue, Minimum(declaration), Maximum(declaration))));
                            break;
                        case VfxParamType.Color:
                            result.Set(VfxParamValue.MakeColor(candidate.name, candidate.colorValue));
                            break;
                        case VfxParamType.Bool:
                            result.Set(VfxParamValue.MakeBool(candidate.name, candidate.boolValue));
                            break;
                    }
                }
            }

            return result;
        }

        public static bool IsDeclared(VfxParamDeclaration[] declarations, string name, out VfxParamDeclaration declaration)
        {
            declaration = FindDeclaration(declarations, name);
            return declaration != null;
        }

        static VfxParamDeclaration FindDeclaration(VfxParamDeclaration[] declarations, string name)
        {
            if (declarations == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            for (int i = 0; i < declarations.Length; i++)
            {
                var declaration = declarations[i];
                if (declaration != null && string.Equals(declaration.name, name, StringComparison.Ordinal))
                {
                    return declaration;
                }
            }

            return null;
        }

        static float Minimum(VfxParamDeclaration declaration)
        {
            return Mathf.Min(declaration.minValue, declaration.maxValue);
        }

        static float Maximum(VfxParamDeclaration declaration)
        {
            return Mathf.Max(declaration.minValue, declaration.maxValue);
        }
    }
}
