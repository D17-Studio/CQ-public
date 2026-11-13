using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using CQ_ChartMaker.ViewModels;

namespace CQ_ChartMaker.Views;

public partial class ChartView : UserControl
{
    private Action<TimeSpan>? _renderLoop;
    private bool _renderLoopRunning;
    private int _renderLoopGeneration; // 每次启动/停止递增，旧循环在途回调查代际后自行退出

    public ChartView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    private void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        // 防止重复启动：视图被重新附加时不叠加第二个渲染循环
        if (_renderLoopRunning) return;
        _renderLoopRunning = true;

        int generation = ++_renderLoopGeneration;
        _renderLoop = _ =>
        {
            if (generation != _renderLoopGeneration) return; // 已停止/重启：不再申请下一帧

            LogFrameStats(); // 临时性能诊断，验证完删除

            if (DataContext is ChartViewModel vm)
            {
                long updateStart = Stopwatch.GetTimestamp();
                vm.OnFrameUpdate();
                FrameDiag.AddUpdate(Stopwatch.GetTimestamp() - updateStart);
            }

            topLevel.RequestAnimationFrame(_renderLoop);
        };
        topLevel.RequestAnimationFrame(_renderLoop);
    }

    private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // 停止当前循环：递增代际让在途回调失效，下次附加时重新启动
        _renderLoopRunning = false;
        _renderLoopGeneration++;
    }

    // === 临时性能诊断：每秒汇总帧间隔 / GC / 分配量，后台线程写文件（验证完删除） ===

    private readonly ConcurrentQueue<string> _logQueue = new();
    private bool _logWriterStarted;
    private long _lastFrameTimestamp;
    private long _lastLogTimestamp;
    private int _frameCount;
    private double _maxIntervalMs;
    private int _over10Ms;
    private int _over20Ms;
    private int _lastGc0, _lastGc1, _lastGc2;
    private long _lastAllocatedBytes;

    private void LogFrameStats()
    {
        long now = Stopwatch.GetTimestamp();

        if (_lastFrameTimestamp != 0)
        {
            double intervalMs = (now - _lastFrameTimestamp) * 1000.0 / Stopwatch.Frequency;
            _frameCount++;
            if (intervalMs > _maxIntervalMs) _maxIntervalMs = intervalMs;
            if (intervalMs > 10) _over10Ms++;
            if (intervalMs > 20) _over20Ms++;
        }
        _lastFrameTimestamp = now;

        if (_lastLogTimestamp == 0) _lastLogTimestamp = now;

        double elapsedMs = (now - _lastLogTimestamp) * 1000.0 / Stopwatch.Frequency;
        if (elapsedMs < 1000) return;

        int gc0 = GC.CollectionCount(0);
        int gc1 = GC.CollectionCount(1);
        int gc2 = GC.CollectionCount(2);
        long allocated = GC.GetTotalAllocatedBytes(false);

        // 最近一次 GC 的暂停时长（多个阶段求和），用于确认长帧是否来自 GC
        double lastGcPauseMs = 0;
        foreach (var pause in GC.GetGCMemoryInfo().PauseDurations)
            lastGcPauseMs += pause.TotalMilliseconds;

        // 每帧耗时：OnFrameUpdate（含编辑区刷新）与 GameCanvas 绘制
        double updateAvgMs = FrameDiag.UpdateCount == 0 ? 0 :
            FrameDiag.UpdateTicks * 1000.0 / Stopwatch.Frequency / FrameDiag.UpdateCount;
        double updateMaxMs = FrameDiag.UpdateMaxTicks * 1000.0 / Stopwatch.Frequency;
        double renderAvgMs = FrameDiag.RenderCount == 0 ? 0 :
            FrameDiag.RenderTicks * 1000.0 / Stopwatch.Frequency / FrameDiag.RenderCount;
        double renderMaxMs = FrameDiag.RenderMaxTicks * 1000.0 / Stopwatch.Frequency;
        FrameDiag.Reset();

        EnqueueLog(
            $"[帧统计] FPS={_frameCount * 1000.0 / elapsedMs:F1} 最大间隔={_maxIntervalMs:F1}ms " +
            $">10ms={_over10Ms} >20ms={_over20Ms} | " +
            $"GC0={gc0 - _lastGc0} GC1={gc1 - _lastGc1} GC2={gc2 - _lastGc2} " +
            $"分配={(allocated - _lastAllocatedBytes) / 1024.0 / 1024.0:F2}MB/s 最后GC暂停={lastGcPauseMs:F1}ms " +
            $"更新={updateAvgMs:F1}/{updateMaxMs:F1}ms 绘制={renderAvgMs:F1}/{renderMaxMs:F1}ms");

        _frameCount = 0;
        _maxIntervalMs = 0;
        _over10Ms = 0;
        _over20Ms = 0;
        _lastGc0 = gc0;
        _lastGc1 = gc1;
        _lastGc2 = gc2;
        _lastAllocatedBytes = allocated;
        _lastLogTimestamp = now;
    }

    /// <summary> 入队日志并确保后台写入线程已启动（文件写入不占用 UI 线程） </summary>
    private void EnqueueLog(string line)
    {
        _logQueue.Enqueue(line);
        if (_logWriterStarted) return;
        _logWriterStarted = true;
        Task.Run(WriteLogLoop);
    }

    /// <summary> 后台日志写入循环：新建本会话日志文件，之后每 200ms 追加一次 </summary>
    private async Task WriteLogLoop()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "frame_stats.log");
        bool firstWrite = true;

        while (true)
        {
            try
            {
                if (firstWrite)
                {
                    File.WriteAllText(path, $"[会话开始] {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
                    firstWrite = false;
                }

                while (_logQueue.TryDequeue(out string? line))
                    File.AppendAllText(path, line + "\n");
            }
            catch
            {
                // 诊断日志写入失败不影响主程序
            }

            await Task.Delay(200);
        }
    }
}

/// <summary> 临时诊断：汇总每帧更新与绘制耗时（验证完删除） </summary>
internal static class FrameDiag
{
    public static long UpdateCount;
    public static long UpdateTicks;
    public static long UpdateMaxTicks;
    public static long RenderCount;
    public static long RenderTicks;
    public static long RenderMaxTicks;

    public static void AddUpdate(long ticks)
    {
        UpdateCount++;
        UpdateTicks += ticks;
        if (ticks > UpdateMaxTicks) UpdateMaxTicks = ticks;
    }

    public static void AddRender(long ticks)
    {
        RenderCount++;
        RenderTicks += ticks;
        if (ticks > RenderMaxTicks) RenderMaxTicks = ticks;
    }

    public static void Reset()
    {
        UpdateCount = UpdateTicks = UpdateMaxTicks = 0;
        RenderCount = RenderTicks = RenderMaxTicks = 0;
    }
}
