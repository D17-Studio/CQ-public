using System.Text;
using System.Text.Json;
using CQ.Vn;

namespace CQ.Vn.Host;

public sealed class StoryBundle
{
    public StoryBundle(VnProgram program, VnCharacterTable characters, string assetDirectory)
    {
        Program = program;
        Characters = characters;
        AssetDirectory = assetDirectory;
    }

    public VnProgram Program { get; }
    public VnCharacterTable Characters { get; }
    public string AssetDirectory { get; }
    public string Title => Program.Title;
}

public static class StoryAssets
{
    public static StoryBundle Load()
    {
        string directory = FindAssetDirectory();
        string scriptPath = Path.Combine(directory, "story.vns");
        string characterPath = Path.Combine(directory, "characters.json");

        VnProgram program = VnScriptParser.Parse(File.ReadAllText(scriptPath, Encoding.UTF8));

        var characters = new List<VnCharacter>();
        if (File.Exists(characterPath))
        {
            CharacterFile? file = JsonSerializer.Deserialize<CharacterFile>(
                File.ReadAllText(characterPath, Encoding.UTF8),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (file?.Characters != null)
            {
                foreach (CharacterEntry? entry in file.Characters)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Id))
                    {
                        continue;
                    }

                    characters.Add(new VnCharacter(entry.Id, entry.Name, entry.Color, entry.DefaultExpression));
                }
            }
        }

        return new StoryBundle(program, new VnCharacterTable(characters), directory);
    }

    private static string FindAssetDirectory()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "assets"),
            Path.Combine(Directory.GetCurrentDirectory(), "assets")
        };

        string? current = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && !string.IsNullOrEmpty(current); depth++)
        {
            candidates.Add(Path.Combine(current, "assets"));
            current = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar));
        }

        foreach (string candidate in candidates)
        {
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "story.vns")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("找不到 assets 目录（需要 story.vns 和 characters.json）。");
    }
}

internal sealed class CharacterFile
{
    public List<CharacterEntry?>? Characters { get; set; }
}

internal sealed class CharacterEntry
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Color { get; set; }
    public string? DefaultExpression { get; set; }
}
