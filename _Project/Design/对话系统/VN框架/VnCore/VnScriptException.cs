using System;

namespace CQ.Vn
{
    public sealed class VnScriptException : Exception
    {
        public VnScriptException(string message, int line = 0)
            : base(line > 0 ? $"第 {line} 行：{message}" : message)
        {
            Line = line;
        }

        public int Line { get; }
    }

    public sealed class VnRuntimeException : Exception
    {
        public VnRuntimeException(string message, int sourceLine = 0)
            : base(sourceLine > 0 ? $"第 {sourceLine} 行：{message}" : message)
        {
            SourceLine = sourceLine;
        }

        public int SourceLine { get; }
    }
}
