using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

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
    /// 在指定父目录下创建项目文件夹，内含空白的 chart.txt
    /// </summary>
    /// <param name="parentPath">父目录路径</param>
    /// <param name="projectName">项目名称</param>
    /// <returns>创建的项目文件夹路径</returns>
    public static string CreateProject(string parentPath, string projectName)
    {
        string projectPath = Path.Combine(parentPath, projectName);
        Directory.CreateDirectory(projectPath);

        string chartPath = Path.Combine(projectPath, "chart.txt");
        File.WriteAllText(chartPath, string.Empty);

        return projectPath;
    }

    /// <summary>
    /// 保存 chart.txt 到项目文件夹
    /// </summary>
    public static void SaveProject(string projectPath, string chartText)
    {
        string chartPath = Path.Combine(projectPath, "chart.txt");
        File.WriteAllText(chartPath, chartText);
    }

    /// <summary>
    /// 打开项目文件夹，读取 chart.txt 的内容
    /// </summary>
    /// <param name="projectPath">项目文件夹路径</param>
    /// <returns>chart.txt 的文本内容</returns>
    /// <exception cref="FileNotFoundException">chart.txt 不存在时抛出</exception>
    public static string OpenProject(string projectPath)
    {
        string chartPath = Path.Combine(projectPath, "chart.txt");
        if (!File.Exists(chartPath))
            throw new FileNotFoundException("项目文件夹中未找到 chart.txt", chartPath);

        return File.ReadAllText(chartPath);
    }

    /// <summary>
    /// 项目内记录音频路径的文件名（与 chart.txt 同层级）
    /// </summary>
    private const string AudioPathFileName = "audioPath.txt";

    /// <summary>
    /// 保存音频文件路径到项目文件夹（写入 audioPath.txt，已有内容会被覆盖）
    /// </summary>
    public static void SaveAudioPath(string projectPath, string audioPath)
    {
        File.WriteAllText(Path.Combine(projectPath, AudioPathFileName), audioPath);
    }

    /// <summary>
    /// 读取项目记录的音频文件路径
    /// </summary>
    /// <returns>记录的路径；无记录或读取失败时返回 null</returns>
    public static string? LoadAudioPath(string projectPath)
    {
        try
        {
            string filePath = Path.Combine(projectPath, AudioPathFileName);
            if (!File.Exists(filePath)) return null;

            string text = File.ReadAllText(filePath).Trim();
            return text.Length > 0 ? text : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadAudioPath] 失败: {ex.Message}");
            return null;
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
