using System;
using System.Collections.Generic;
using System.Globalization;

namespace CQ.Vn
{
    public sealed class VnOperand
    {
        private VnOperand(bool isVariable, string name, VnValue literal)
        {
            IsVariable = isVariable;
            Name = name;
            Literal = literal;
        }

        public bool IsVariable { get; }
        public string Name { get; }
        public VnValue Literal { get; }

        public static VnOperand Variable(string name) => new VnOperand(true, name, VnValue.Default);
        public static VnOperand Constant(VnValue value) => new VnOperand(false, null, value);

        public VnValue Evaluate(IReadOnlyDictionary<string, VnValue> variables)
        {
            if (!IsVariable)
            {
                return Literal;
            }

            return variables != null && variables.TryGetValue(Name, out VnValue value)
                ? value
                : VnValue.Default;
        }

        public static bool TryParse(string token, out VnOperand operand, out string error)
        {
            operand = null;
            error = null;

            if (string.IsNullOrWhiteSpace(token))
            {
                error = "缺少操作数";
                return false;
            }

            token = token.Trim();

            if (token.Length >= 2 && token[0] == '"' && token[token.Length - 1] == '"')
            {
                operand = Constant(VnValue.FromString(token.Substring(1, token.Length - 2)));
                return true;
            }

            if (token.IndexOf('"') >= 0)
            {
                error = "字符串需要用一对双引号包裹";
                return false;
            }

            if (string.Equals(token, "true", StringComparison.OrdinalIgnoreCase))
            {
                operand = Constant(VnValue.FromBoolean(true));
                return true;
            }

            if (string.Equals(token, "false", StringComparison.OrdinalIgnoreCase))
            {
                operand = Constant(VnValue.FromBoolean(false));
                return true;
            }

            if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer))
            {
                operand = Constant(VnValue.FromInteger(integer));
                return true;
            }

            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                operand = Constant(VnValue.FromFloat(number));
                return true;
            }

            if (token.IndexOfAny(new[] { ' ', '\t' }) >= 0)
            {
                error = $"无法识别操作数「{token}」";
                return false;
            }

            operand = Variable(token);
            return true;
        }
    }

    public enum VnComparisonOperator
    {
        Equal,
        NotEqual,
        Greater,
        GreaterOrEqual,
        Less,
        LessOrEqual
    }

    public sealed class VnCondition
    {
        private VnCondition(VnOperand left, VnComparisonOperator? comparison, VnOperand right)
        {
            Left = left;
            Operator = comparison;
            Right = right;
        }

        public VnOperand Left { get; }
        public VnComparisonOperator? Operator { get; }
        public VnOperand Right { get; }

        public static VnCondition FromTruthy(VnOperand operand) => new VnCondition(operand, null, null);

        public static VnCondition FromComparison(VnOperand left, VnComparisonOperator comparison, VnOperand right) =>
            new VnCondition(left, comparison, right);

        public bool Evaluate(IReadOnlyDictionary<string, VnValue> variables)
        {
            VnValue left = Left.Evaluate(variables);
            if (!Operator.HasValue)
            {
                return left.IsTruthy();
            }

            VnValue right = Right.Evaluate(variables);
            switch (Operator.Value)
            {
                case VnComparisonOperator.Equal:
                    return left.ValueEquals(right);
                case VnComparisonOperator.NotEqual:
                    return !left.ValueEquals(right);
            }

            int? comparison = VnValue.Compare(left, right);
            if (!comparison.HasValue)
            {
                return false;
            }

            switch (Operator.Value)
            {
                case VnComparisonOperator.Greater:
                    return comparison.Value > 0;
                case VnComparisonOperator.GreaterOrEqual:
                    return comparison.Value >= 0;
                case VnComparisonOperator.Less:
                    return comparison.Value < 0;
                case VnComparisonOperator.LessOrEqual:
                    return comparison.Value <= 0;
                default:
                    return false;
            }
        }

        public static bool TryParse(string text, out VnCondition condition, out string error)
        {
            condition = null;
            error = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                error = "条件不能为空";
                return false;
            }

            int index = 0;
            if (!TryReadToken(text, ref index, out string leftToken, out error))
            {
                return false;
            }

            SkipWhitespace(text, ref index);
            if (index >= text.Length)
            {
                if (!VnOperand.TryParse(leftToken, out VnOperand single, out error))
                {
                    return false;
                }

                condition = FromTruthy(single);
                return true;
            }

            if (!TryReadComparison(text, ref index, out VnComparisonOperator? comparison, out error))
            {
                return false;
            }

            SkipWhitespace(text, ref index);
            if (index >= text.Length)
            {
                error = "比较运算符后缺少操作数";
                return false;
            }

            if (!TryReadToken(text, ref index, out string rightToken, out error))
            {
                return false;
            }

            SkipWhitespace(text, ref index);
            if (index < text.Length)
            {
                error = $"条件末尾有多余内容「{text.Substring(index)}」";
                return false;
            }

            if (!VnOperand.TryParse(leftToken, out VnOperand left, out error) ||
                !VnOperand.TryParse(rightToken, out VnOperand right, out error))
            {
                return false;
            }

            condition = FromComparison(left, comparison.Value, right);
            return true;
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }
        }

        private static bool TryReadToken(string text, ref int index, out string token, out string error)
        {
            token = null;
            error = null;

            SkipWhitespace(text, ref index);
            if (index >= text.Length)
            {
                error = "条件不完整";
                return false;
            }

            if (text[index] == '"')
            {
                int start = index;
                index++;
                while (index < text.Length && text[index] != '"')
                {
                    index++;
                }

                if (index >= text.Length)
                {
                    error = "字符串缺少结束引号";
                    return false;
                }

                index++;
                token = text.Substring(start, index - start);
                return true;
            }

            int tokenStart = index;
            while (index < text.Length &&
                   !char.IsWhiteSpace(text[index]) &&
                   text[index] != '=' &&
                   text[index] != '<' &&
                   text[index] != '>' &&
                   text[index] != '!')
            {
                index++;
            }

            if (index == tokenStart)
            {
                error = "缺少操作数";
                return false;
            }

            token = text.Substring(tokenStart, index - tokenStart);
            return true;
        }

        private static bool TryReadComparison(string text, ref int index, out VnComparisonOperator? comparison, out string error)
        {
            comparison = null;
            error = null;

            if (index >= text.Length)
            {
                return true;
            }

            string remaining = text.Substring(index);
            if (remaining.StartsWith("==", StringComparison.Ordinal))
            {
                comparison = VnComparisonOperator.Equal;
                index += 2;
                return true;
            }

            if (remaining.StartsWith("!=", StringComparison.Ordinal))
            {
                comparison = VnComparisonOperator.NotEqual;
                index += 2;
                return true;
            }

            if (remaining.StartsWith(">=", StringComparison.Ordinal))
            {
                comparison = VnComparisonOperator.GreaterOrEqual;
                index += 2;
                return true;
            }

            if (remaining.StartsWith("<=", StringComparison.Ordinal))
            {
                comparison = VnComparisonOperator.LessOrEqual;
                index += 2;
                return true;
            }

            if (remaining[0] == '>')
            {
                comparison = VnComparisonOperator.Greater;
                index++;
                return true;
            }

            if (remaining[0] == '<')
            {
                comparison = VnComparisonOperator.Less;
                index++;
                return true;
            }

            error = $"无法识别的比较运算符「{remaining}」";
            return false;
        }
    }
}
