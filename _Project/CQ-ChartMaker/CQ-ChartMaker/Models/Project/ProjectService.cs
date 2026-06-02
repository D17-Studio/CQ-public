using System.IO;

namespace CQ_ChartMaker.Models;

/// <summary>
/// 项目文件服务：负责项目的创建与打开（纯文件 I/O，不含 UI 逻辑）
/// </summary>
public static class ProjectService
{
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
}
