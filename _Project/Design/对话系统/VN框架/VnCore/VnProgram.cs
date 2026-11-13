using System;
using System.Collections.Generic;

namespace CQ.Vn
{
    public sealed class VnProgram
    {
        private readonly Dictionary<string, int> _labels;

        public VnProgram(
            string title,
            IReadOnlyList<VnInstruction> instructions,
            IReadOnlyDictionary<string, int> labels,
            int entryIndex)
        {
            Title = title ?? string.Empty;
            Instructions = instructions ?? throw new ArgumentNullException(nameof(instructions));
            _labels = new Dictionary<string, int>(labels, StringComparer.Ordinal);
            EntryIndex = entryIndex;
        }

        public string Title { get; }
        public IReadOnlyList<VnInstruction> Instructions { get; }
        public IReadOnlyDictionary<string, int> Labels => _labels;
        public int EntryIndex { get; }

        public bool TryGetLabel(string label, out int index)
        {
            if (!string.IsNullOrEmpty(label))
            {
                return _labels.TryGetValue(label, out index);
            }

            index = -1;
            return false;
        }
    }
}
