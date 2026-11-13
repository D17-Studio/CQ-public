using System.Threading.Tasks;
using Avalonia.Controls;
using CQ_ChartMaker.ViewModels;

namespace CQ_ChartMaker.Views;

public partial class ChangelogView : Window
{
    public ChangelogView()
    {
        InitializeComponent();
    }

    /// <summary> 以模态对话框形式显示（更新日志窗口无返回值） </summary>
    public async Task ShowDialogAsync(Window owner, ChangelogViewModel vm)
    {
        DataContext = vm;
        await ShowDialog(owner);
    }
}
