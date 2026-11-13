using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CQ_ChartMaker.Views;

public partial class DeleteChartConfirmView : Window
{
    public DeleteChartConfirmView()
    {
        InitializeComponent();
    }

    /// <summary> 以模态方式显示，返回 true=删除 / false=取消 </summary>
    public async Task<bool> ShowDialogAsync(Window owner, string message)
    {
        MessageText.Text = message;
        return await ShowDialog<bool>(owner);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
