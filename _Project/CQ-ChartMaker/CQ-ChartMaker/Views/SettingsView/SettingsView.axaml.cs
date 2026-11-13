using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CQ_ChartMaker.Models;
using CQ_ChartMaker.ViewModels;

namespace CQ_ChartMaker.Views;

public partial class SettingsView : Window
{
    // 捕获键松开时需要吞掉 KeyUp：
    // 否则焦点所在的键位按钮可能把 Space/Enter 的松开当成一次点击，重新进入捕获状态
    private bool _swallowNextKeyUp;

    public SettingsView()
    {
        InitializeComponent();

        // 改键捕获：隧道阶段先于窗口内其他控件收到按键
        AddHandler(KeyDownEvent, OnCaptureKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnCaptureKeyUp, RoutingStrategies.Tunnel);
    }

    /// <summary> 以模态对话框形式显示（设置窗口无返回值） </summary>
    public async Task ShowDialogAsync(Window owner, SettingsViewModel vm)
    {
        DataContext = vm;

        // 把窗口的关闭方法通过委托交给 ViewModel，
        // 使 VM 中的“应用/取消”命令无需直接引用 View 也能关闭窗口
        vm.CloseWindow = () => Close();

        // 打击音效文件选择：StorageProvider 提供系统原生选择器（仅过滤 WAV）
        vm.BrowseHitSoundFile = async () =>
        {
            var options = new FilePickerOpenOptions
            {
                Title = "选择打击音效",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("WAV 音频") { Patterns = new[] { "*.wav" } }
                }
            };

            // 起始位置 = 当前已填路径所在文件夹（为空或无效时使用系统默认位置）
            if (!string.IsNullOrWhiteSpace(vm.HitSoundPath))
            {
                string? folder = Path.GetDirectoryName(vm.HitSoundPath);
                if (!string.IsNullOrEmpty(folder))
                    options.SuggestedStartLocation =
                        await owner.StorageProvider.TryGetFolderFromPathAsync(folder);
            }

            var files = await owner.StorageProvider.OpenFilePickerAsync(options);
            return files.Count > 0 ? files[0].Path.LocalPath : null;
        };

        // 新建项目默认路径：StorageProvider 提供系统原生文件夹选择器
        vm.BrowseDefaultProjectFolder = async () =>
        {
            var options = new FolderPickerOpenOptions { Title = "选择新建项目默认路径" };

            // 起始位置 = 当前已填路径（为空或无效时使用系统默认位置）
            if (!string.IsNullOrWhiteSpace(vm.DefaultProjectPath))
                options.SuggestedStartLocation =
                    await owner.StorageProvider.TryGetFolderFromPathAsync(vm.DefaultProjectPath);

            var folders = await owner.StorageProvider.OpenFolderPickerAsync(options);
            return folders.Count > 0 ? folders[0].Path.LocalPath : null;
        };

        await ShowDialog(owner);
    }

    /// <summary> 左侧分类按钮：滚动到对应的设置区（只定位，不产生选中态） </summary>
    private void OnSectionButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string sectionName)
            return;

        if (this.FindControl<Control>(sectionName) is not { } section)
            return;

        if (this.FindControl<Control>("SectionHost") is not { } host ||
            this.FindControl<ScrollViewer>("SettingsScroll") is not { } scroll)
            return;

        // TranslatePoint 把“区左上角”换算成相对滚动内容宿主的坐标，
        // 即该区在滚动内容中的纵向起点，直接作为滚动偏移即可跳转
        Point? position = section.TranslatePoint(new Point(0, 0), host);
        if (position is null)
            return;

        scroll.Offset = new Vector(0, position.Value.Y);
    }

    /// <summary> 捕获等待中的新键：Esc 取消，纯修饰键忽略，其余键写入当前行 </summary>
    private void OnCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm || vm.CapturingRow == null)
            return;

        e.Handled = true;
        _swallowNextKeyUp = true;

        if (e.Key == Key.Escape)
        {
            vm.CancelCaptureCommand.Execute(null);
            return;
        }

        if (!ShortcutCatalog.IsModifierOnly(e.Key))
            vm.ApplyCapture(e.Key, e.KeyModifiers);
    }

    /// <summary> 吞掉捕获按键的 KeyUp，避免焦点按钮把 Space/Enter 松开误判为点击 </summary>
    private void OnCaptureKeyUp(object? sender, KeyEventArgs e)
    {
        if (!_swallowNextKeyUp)
            return;

        _swallowNextKeyUp = false;
        e.Handled = true;
    }
}
