using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQ_ChartMaker.Models;
using System;
using System.Threading.Tasks;

namespace CQ_ChartMaker.ViewModels;

public partial class CreateProjectViewModel : ObservableObject
{
    // 代码正在写入默认项目名时为 true（用于区分用户手动编辑）
    private bool _isApplyingDefaultName;

    // 用户是否手动修改过项目名（改过则浏览换路径时不再覆盖）
    private bool _userEditedName;

    [ObservableProperty]
    private string _projectName = string.Empty;

    [ObservableProperty]
    private string _projectPath = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// 由 View 层注入的委托：打开文件夹选择器，返回所选路径（用户取消则返回 null）
    /// </summary>
    public Func<Task<string?>>? BrowseFolder { get; set; }

    /// <summary>
    /// 由 View 层注入的委托：关闭窗口，参数 true=确认 / false=取消 / null=关闭
    /// </summary>
    public Action<bool?>? CloseWindow { get; set; }

    /// <summary>
    /// 初始化默认值：保存路径取上次使用的父目录，项目名按该目录自动编号
    /// </summary>
    /// <param name="lastParentPath">上次新建项目使用的父目录，null/空表示无记录</param>
    public void Initialize(string? lastParentPath)
    {
        ProjectPath = lastParentPath ?? string.Empty;
        ApplyDefaultName();
    }

    /// <summary>
    /// 按当前保存路径计算默认项目名（ChartProject1、ChartProject2……）
    /// </summary>
    private void ApplyDefaultName()
    {
        _isApplyingDefaultName = true;
        ProjectName = ProjectService.GetDefaultProjectName(ProjectPath);
        _isApplyingDefaultName = false;
    }

    partial void OnProjectNameChanged(string value)
    {
        // 代码自动写入默认名时不标记为用户编辑
        if (!_isApplyingDefaultName)
            _userEditedName = true;
    }

    [RelayCommand]
    private async Task Browse()
    {
        if (BrowseFolder is not null)
        {
            var path = await BrowseFolder();
            if (path is not null)
            {
                ProjectPath = path;

                // 用户没手动改过名字时，按新路径重新计算默认名
                if (!_userEditedName)
                    ApplyDefaultName();
            }
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(ProjectName))
        {
            ErrorMessage = "请输入项目名称";
            return;
        }
        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            ErrorMessage = "请选择保存路径";
            return;
        }
        // 目标文件夹已存在时提示，避免覆盖已有项目
        if (ProjectService.ProjectExists(ProjectPath, ProjectName))
        {
            ErrorMessage = "项目文件夹已存在，请修改项目名称";
            return;
        }
        ErrorMessage = string.Empty;
        CloseWindow?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseWindow?.Invoke(false);
    }
}
