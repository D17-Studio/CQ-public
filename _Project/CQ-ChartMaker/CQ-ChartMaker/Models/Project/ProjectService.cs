using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.Models;

/// <summary>
/// 最近项目的一条记录
/// </summary>
public class RecentProjectEntry
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTime LastOpened { get; set; }

    public RecentProjectEntry() { }

    public RecentProjectEntry(string name, string path, DateTime lastOpened)
    {
        Name = name;
        Path = path;
        LastOpened = lastOpened;
    }
}

/// <summary>
/// 下拉框中的一条谱面记录（FileName 用于文件操作，DisplayName 用于显示，IsCurrent 控制高亮）
/// </summary>
public partial class ChartFileEntry : ObservableObject
{
    public string FileName { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private bool _isCurrent;

    public ChartFileEntry(string fileName, bool isCurrent)
    {
        FileName = fileName;
        DisplayName = Path.GetFileNameWithoutExtension(fileName);
        IsCurrent = isCurrent;
    }
}

/// <summary>
/// 项目文件服务：负责项目的创建与打开、最近项目列表的持久化（纯文件 I/O，不含 UI 逻辑）
/// </summary>
public static class ProjectService
{
    /// <summary>
    /// 最近项目列表 JSON 文件的存储路径（%LocalAppData%/CQ-ChartMaker/recent_projects.json）
    /// </summary>
    private static string RecentProjectsFilePath
    {
        get
        {
            string dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CQ-ChartMaker");
            Directory.CreateDirectory(dir); // 确保目录存在
            return System.IO.Path.Combine(dir, "recent_projects.json");
        }
    }

    /// <summary>
    /// 项目内存放谱面的子文件夹名
    /// </summary>
    private const string ChartsDirectoryName = "charts";

    /// <summary>
    /// 获取项目的谱面文件夹路径（charts）
    /// </summary>
    public static string GetChartsDirectory(string projectPath)
        => Path.Combine(projectPath, ChartsDirectoryName);

    /// <summary>
    /// 判断文件夹是否为有效项目：必须存在 charts 文件夹（旧版根目录 chart.txt 不再兼容）
    /// </summary>
    public static bool IsValidProject(string projectPath)
        => Directory.Exists(GetChartsDirectory(projectPath));

    /// <summary>
    /// 在指定父目录下创建项目文件夹，并建立空的 charts 文件夹与默认谱面 chart1.txt
    /// </summary>
    /// <param name="parentPath">父目录路径</param>
    /// <param name="projectName">项目名称</param>
    /// <returns>创建的项目文件夹路径</returns>
    public static string CreateProject(string parentPath, string projectName)
    {
        string projectPath = Path.Combine(parentPath, projectName);
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(GetChartsDirectory(projectPath));

        // 新建项目先放一份空白谱面，打开后即可直接编辑
        CreateChart(projectPath, GetDefaultChartName(projectPath));

        // 曲目信息：用序列化器写一份空白 JSON 模板
        SaveTrackInfo(projectPath, new TrackInfo());
        return projectPath;
    }

    /// <summary>
    /// 列出项目 charts 文件夹下的所有谱面文件（仅顶层 .txt，按文件名排序）
    /// </summary>
    public static List<string> ListCharts(string projectPath)
    {
        string chartsDir = GetChartsDirectory(projectPath);
        if (!Directory.Exists(chartsDir))
            return new List<string>();

        return Directory.GetFiles(chartsDir, "*.txt", SearchOption.TopDirectoryOnly)
            .Select(p => Path.GetFileName(p) ?? string.Empty)
            .Where(n => n.Length > 0)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 计算默认谱面名：从 chart1 开始，返回 charts 下第一个不存在的编号
    /// </summary>
    public static string GetDefaultChartName(string projectPath)
    {
        const string prefix = "chart";
        string chartsDir = GetChartsDirectory(projectPath);
        int index = 1;

        if (Directory.Exists(chartsDir))
        {
            while (File.Exists(Path.Combine(chartsDir, prefix + index + ".txt")))
                index++;
        }

        return prefix + index;
    }

    /// <summary>
    /// 创建空白谱面文件（参数为不含扩展名的名称），返回实际文件名（name.txt）。
    /// 内容是合法的最小 JSON 框架（BPM 0 = 200），新建后即可直接编辑，不会产生非法文件。
    /// </summary>
    public static string CreateChart(string projectPath, string chartName)
    {
        string chartsDir = GetChartsDirectory(projectPath);
        Directory.CreateDirectory(chartsDir);

        var chart = new ChartSheet();
        chart.GetBpmList().Add(new BpmData { StartTime = 0, Bpm = 200f }); // 无 BPM 时补一个默认点，保证节拍线与吸附可用

        string fileName = chartName + ".txt";
        File.WriteAllText(Path.Combine(chartsDir, fileName), ChartSerializer.Serialize(chart));
        return fileName;
    }

    /// <summary>
    /// 读取 charts 文件夹中指定谱面文件的内容
    /// </summary>
    /// <exception cref="FileNotFoundException">谱面文件不存在时抛出</exception>
    public static string LoadChart(string projectPath, string fileName)
    {
        string chartPath = Path.Combine(GetChartsDirectory(projectPath), fileName);
        if (!File.Exists(chartPath))
            throw new FileNotFoundException($"项目 charts 文件夹中未找到谱面 {fileName}", chartPath);

        return File.ReadAllText(chartPath);
    }

    /// <summary>
    /// 保存谱面文本到 charts 文件夹中的指定文件
    /// </summary>
    public static void SaveChart(string projectPath, string fileName, string chartText)
    {
        string chartsDir = GetChartsDirectory(projectPath);
        Directory.CreateDirectory(chartsDir);
        File.WriteAllText(Path.Combine(chartsDir, fileName), chartText);
    }

    /// <summary>
    /// 重命名谱面文件（参数为不含扩展名的新名称），返回新文件名（name.txt）
    /// </summary>
    public static string RenameChart(string projectPath, string oldFileName, string newChartName)
    {
        string chartsDir = GetChartsDirectory(projectPath);
        string newFileName = newChartName + ".txt";
        File.Move(Path.Combine(chartsDir, oldFileName), Path.Combine(chartsDir, newFileName));
        return newFileName;
    }

    /// <summary>
    /// 计算默认项目名：从 ChartProject1 开始，返回父目录下第一个不存在的编号
    /// </summary>
    /// <param name="parentPath">项目父目录（不存在时直接返回 ChartProject1）</param>
    public static string GetDefaultProjectName(string parentPath)
    {
        const string prefix = "ChartProject";
        int index = 1;

        if (!string.IsNullOrWhiteSpace(parentPath) && Directory.Exists(parentPath))
        {
            while (Directory.Exists(Path.Combine(parentPath, prefix + index)))
                index++;
        }

        return prefix + index;
    }

    /// <summary>
    /// 判断父目录下是否已存在同名项目文件夹
    /// </summary>
    public static bool ProjectExists(string parentPath, string projectName)
    {
        return Directory.Exists(Path.Combine(parentPath, projectName));
    }

    /// <summary>
    /// 项目根目录下的曲目信息文件名
    /// </summary>
    private const string TrackInfoFileName = "TrackInfo.txt";

    /// <summary>
    /// 获取 TrackInfo.txt 的完整路径
    /// </summary>
    public static string GetTrackInfoPath(string projectPath)
        => Path.Combine(projectPath, TrackInfoFileName);

    /// <summary>
    /// 读取项目曲目信息；文件不存在时创建空白模板
    /// </summary>
    public static TrackInfo LoadTrackInfo(string projectPath)
    {
        string filePath = GetTrackInfoPath(projectPath);
        if (!File.Exists(filePath))
        {
            // 文件不存在时用序列化器写一份空白 JSON 模板
            var info = new TrackInfo();
            SaveTrackInfo(projectPath, info);
            return info;
        }

        try
        {
            return TrackInfoSerializer.Parse(File.ReadAllText(filePath));
        }
        catch (ChartException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadTrackInfo] 解析失败: {ex.Message}");
            return new TrackInfo();
        }
    }

    /// <summary>
    /// 保存曲目信息到项目根目录的 TrackInfo.txt
    /// </summary>
    public static void SaveTrackInfo(string projectPath, TrackInfo info)
    {
        File.WriteAllText(GetTrackInfoPath(projectPath), TrackInfoSerializer.Serialize(info));
    }

    /// <summary>
    /// 列出项目根目录下的曲绘文件（不递归子文件夹）
    /// </summary>
    public static List<string> ListCoverFiles(string projectPath)
        => ListRootFiles(projectPath, ".png", ".jpg", ".jpeg");

    /// <summary>
    /// 列出项目根目录下的 WAV 音频文件（不递归子文件夹）
    /// </summary>
    public static List<string> ListMusicFiles(string projectPath)
        => ListRootFiles(projectPath, ".wav");

    /// <summary>
    /// 从外部导入文件到项目根目录；源文件已在根目录时直接返回文件名。
    /// 同名文件自动改名为 “名称 (1).扩展名”。
    /// </summary>
    /// <returns>项目根目录下的文件名；导入失败返回 null</returns>
    public static string? ImportFileToProject(string projectPath, string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
                return null;

            string root = NormalizeDirectory(Path.GetFullPath(projectPath));
            string source = Path.GetFullPath(sourcePath);
            string? sourceDir = Path.GetDirectoryName(source);

            // 源文件已经在项目根目录：只选择，不复制
            if (sourceDir != null && string.Equals(NormalizeDirectory(sourceDir), root,
                                                  StringComparison.OrdinalIgnoreCase))
                return Path.GetFileName(source);

            string target = Path.Combine(root, Path.GetFileName(source));
            target = GetUniqueFilePath(target);
            File.Copy(source, target);
            return Path.GetFileName(target);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImportFileToProject] 失败: {ex.Message}");
            return null;
        }
    }

    private static List<string> ListRootFiles(string projectPath, params string[] extensions)
    {
        if (!Directory.Exists(projectPath))
            return new List<string>();

        return Directory.GetFiles(projectPath, "*", SearchOption.TopDirectoryOnly)
            .Where(p => extensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .Select(p => Path.GetFileName(p))
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => n!)
            .ToList();
    }

    private static string NormalizeDirectory(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string GetUniqueFilePath(string path)
    {
        if (!File.Exists(path))
            return path;

        string dir = Path.GetDirectoryName(path) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);

        for (int index = 1; ; index++)
        {
            string candidate = Path.Combine(dir, $"{name} ({index}){extension}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    /// <summary>
    /// System.Text.Json 序列化选项：不区分大小写 + 缩进格式
    /// </summary>
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>
    /// 加载最近项目列表（按 LastOpened 降序排列）
    /// </summary>
    public static List<RecentProjectEntry> LoadRecentProjects()
    {
        try
        {
            string path = RecentProjectsFilePath;
            if (!File.Exists(path))
                return new List<RecentProjectEntry>();

            string json = File.ReadAllText(path);
            var entries = JsonSerializer.Deserialize<List<RecentProjectEntry>>(json, _jsonOptions);
            return entries?
                .OrderByDescending(e => e.LastOpened)
                .ToList() ?? new List<RecentProjectEntry>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadRecentProjects] 失败: {ex.Message}");
            return new List<RecentProjectEntry>();
        }
    }

    /// <summary>
    /// 保存最近项目列表到 JSON 文件
    /// </summary>
    private static void SaveRecentProjects(List<RecentProjectEntry> entries)
    {
        try
        {
            string json = JsonSerializer.Serialize(entries, _jsonOptions);
            File.WriteAllText(RecentProjectsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SaveRecentProjects] 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 记录一个项目到最近列表（去重 + 最多保留 10 条）
    /// </summary>
    /// <param name="projectPath">项目文件夹路径</param>
    public static void RecordRecentProject(string projectPath)
    {
        // 文件夹选择器返回的路径末尾可能带 \ 或 /，Path.GetFileName 会返回空串，需要先 Trim
        string normalizedPath = projectPath.TrimEnd(System.IO.Path.DirectorySeparatorChar,
                                                     System.IO.Path.AltDirectorySeparatorChar);
        string name = System.IO.Path.GetFileName(normalizedPath);
        var entries = LoadRecentProjects();

        // 移除同路径的旧记录
        entries.RemoveAll(e => string.Equals(e.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));

        // 插入到最前面
        entries.Insert(0, new RecentProjectEntry(name, normalizedPath, DateTime.Now));

        // 最多保留 10 条
        if (entries.Count > 10)
            entries = entries.Take(10).ToList();

        SaveRecentProjects(entries);
    }

    /// <summary>
    /// 从最近项目列表中移除指定路径的记录
    /// </summary>
    /// <param name="projectPath">要移除的项目文件夹路径</param>
    public static void RemoveRecentProject(string projectPath)
    {
        string normalizedPath = projectPath.TrimEnd(System.IO.Path.DirectorySeparatorChar,
                                                     System.IO.Path.AltDirectorySeparatorChar);
        var entries = LoadRecentProjects();
        entries.RemoveAll(e => string.Equals(e.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));
        SaveRecentProjects(entries);
    }
}
