using System;
using System.Collections.Generic;

namespace CQ.Vn
{
    public sealed class VnCharacter
    {
        public VnCharacter(string id, string displayName, string color, string defaultExpression = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("角色 id 不能为空。", nameof(id));
            }

            Id = id.Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName.Trim();
            Color = string.IsNullOrWhiteSpace(color) ? "#c8d2ff" : color.Trim();
            DefaultExpression = string.IsNullOrWhiteSpace(defaultExpression) ? "默认" : defaultExpression.Trim();
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Color { get; }
        public string DefaultExpression { get; }
    }

    public sealed class VnCharacterTable
    {
        private readonly Dictionary<string, VnCharacter> _characters;

        public VnCharacterTable(IEnumerable<VnCharacter> characters)
        {
            _characters = new Dictionary<string, VnCharacter>(StringComparer.Ordinal);
            if (characters == null)
            {
                return;
            }

            foreach (VnCharacter character in characters)
            {
                if (character != null)
                {
                    _characters[character.Id] = character;
                }
            }
        }

        public static VnCharacterTable Empty { get; } = new VnCharacterTable(null);

        public IReadOnlyDictionary<string, VnCharacter> Characters => _characters;

        public bool TryGet(string id, out VnCharacter character)
        {
            if (!string.IsNullOrEmpty(id) && _characters.TryGetValue(id, out character))
            {
                return true;
            }

            character = null;
            return false;
        }

        public VnCharacter GetOrFallback(string id)
        {
            if (TryGet(id, out VnCharacter character))
            {
                return character;
            }

            string safeId = string.IsNullOrWhiteSpace(id) ? "?" : id.Trim();
            return new VnCharacter(safeId, safeId, "#c8d2ff");
        }
    }
}
