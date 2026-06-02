using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CQ_ChartMaker.ViewModels;

namespace CQ_ChartMaker.Views;

public partial class CreateProjectView : Window
{
    public CreateProjectView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 以模态对话框形式显示，返回 true=确认 / false=取消
    /// </summary>
    public async Task<bool> ShowDialogAsync(Window owner, CreateProjectViewModel vm)
    {
        DataContext = vm;

        vm.BrowseFolder = async () =>
        {
            var folders = await owner.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = "选择项目保存位置" });
            if (folders.Count == 0)
                return null;
            return folders[0].Path.LocalPath;
        };

        vm.CloseWindow = (result) =>
        {
            Close(result ?? false);
        };

        return await ShowDialog<bool>(owner);
    }
}
