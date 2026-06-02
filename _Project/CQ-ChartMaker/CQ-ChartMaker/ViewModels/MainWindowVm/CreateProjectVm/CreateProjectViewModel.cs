using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;

namespace CQ_ChartMaker.ViewModels;

public partial class CreateProjectViewModel : ObservableObject
{
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

    [RelayCommand]
    private async Task Browse()
    {
        if (BrowseFolder is not null)
        {
            var path = await BrowseFolder();
            if (path is not null)
                ProjectPath = path;
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
        ErrorMessage = string.Empty;
        CloseWindow?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseWindow?.Invoke(false);
    }
}
