using System;
using System.Globalization;

namespace CQ.Vn
{
    public enum VnValueKind
    {
        Integer,
        Float,
        Boolean,
        String
    }

    public readonly struct VnValue : IEquatable<VnValue>
    {
        private VnValue(VnValueKind kind, double number, bool boolean, string text)
        {
            Kind = kind;
            Number = number;
            Bool = boolean;
            Text = text;
        }

        public VnValueKind Kind { get; }
        public double Number { get; }
        public bool Bool { get; }
        public string Text { get; }
        public bool IsNumeric => Kind == VnValueKind.Integer || Kind == VnValueKind.Float;

        public static VnValue FromInteger(long value) => new VnValue(VnValueKind.Integer, value, false, null);
        public static VnValue FromFloat(double value) => new VnValue(VnValueKind.Float, value, false, null);
        public static VnValue FromBoolean(bool value) => new VnValue(VnValueKind.Boolean, 0d, value, null);
        public static VnValue FromString(string value) => new VnValue(VnValueKind.String, 0d, false, value ?? string.Empty);
        public static VnValue Default => FromInteger(0);

        public bool IsTruthy()
        {
            switch (Kind)
            {
                case VnValueKind.Integer:
                case VnValueKind.Float:
                    return Number != 0d;
                case VnValueKind.Boolean:
                    return Bool;
                default:
                    return !string.IsNullOrEmpty(Text);
            }
        }

        public bool TryGetNumber(out double value)
        {
            if (IsNumeric)
            {
                value = Number;
                return true;
            }

            value = 0d;
            return false;
        }

        public bool ValueEquals(VnValue other)
        {
            if (IsNumeric && other.IsNumeric)
            {
                return Number.Equals(other.Number);
            }

            if (Kind != other.Kind)
            {
                return false;
            }

            switch (Kind)
            {
                case VnValueKind.Boolean:
                    return Bool == other.Bool;
                case VnValueKind.String:
                    return string.Equals(Text, other.Text, StringComparison.Ordinal);
                default:
                    return Number.Equals(other.Number);
            }
        }

        public static int? Compare(VnValue left, VnValue right)
        {
            if (left.IsNumeric && right.IsNumeric)
            {
                return left.Number.CompareTo(right.Number);
            }

            if (left.Kind == VnValueKind.String && right.Kind == VnValueKind.String)
            {
                return string.CompareOrdinal(left.Text, right.Text);
            }

            if (left.Kind == VnValueKind.Boolean && right.Kind == VnValueKind.Boolean)
            {
                return left.Bool.CompareTo(right.Bool);
            }

            return null;
        }

        public bool Equals(VnValue other) => ValueEquals(other);

        public override bool Equals(object obj) => obj is VnValue other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind * 397;
                if (IsNumeric)
                {
                    hash ^= Number.GetHashCode();
                }
                else
                {
                    hash ^= Bool.GetHashCode();
                    hash ^= Text != null ? Text.GetHashCode() : 0;
                }

                return hash;
            }
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case VnValueKind.Integer:
                    return ((long)Number).ToString(CultureInfo.InvariantCulture);
                case VnValueKind.Float:
                    return Number.ToString("G", CultureInfo.InvariantCulture);
                case VnValueKind.Boolean:
                    return Bool ? "true" : "false";
                default:
                    return Text ?? string.Empty;
            }
        }
    }
}
