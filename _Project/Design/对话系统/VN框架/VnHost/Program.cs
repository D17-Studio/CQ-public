using System.Text.Json;
using CQ.Vn;
using CQ.Vn.Host;

if (args.Contains("--selftest"))
{
    try
    {
        StoryBundle selftestStory = StoryAssets.Load();
        Environment.ExitCode = SelfTest.Run(selftestStory);
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine("自测失败：" + exception.Message);
        Environment.ExitCode = 1;
    }

    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5177");
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

StoryBundle story;
try
{
    story = StoryAssets.Load();
}
catch (Exception exception)
{
    Console.Error.WriteLine("加载剧情资产失败：" + exception.Message);
    Environment.ExitCode = 1;
    return;
}

builder.Services.AddSingleton(story);
builder.Services.AddSingleton<VnSession>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception exception) when (
        exception is VnScriptException or VnRuntimeException or ArgumentException or InvalidOperationException)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = exception.Message });
    }
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/bootstrap", (VnSession session) => session.Bootstrap());
app.MapPost("/api/start", (StartRequest? request, VnSession session) => session.Start(request?.Label));
app.MapPost("/api/advance", (VnSession session) => session.Advance());
app.MapPost("/api/choose", (ChooseRequest request, VnSession session) => session.Choose(request.Index));
app.MapPost("/api/jump", (JumpRequest request, VnSession session) => session.JumpTo(request.Label));
app.MapPost("/api/save", (VnSession session) => session.Save());
app.MapPost("/api/load", (LoadRequest request, VnSession session) => session.Load(request.Snapshot));

Console.WriteLine();
Console.WriteLine($"剧情：{story.Title}");
Console.WriteLine($"脚本：{story.Program.Instructions.Count} 条指令 / {story.Program.Labels.Count} 个标签");
Console.WriteLine($"资源：{story.AssetDirectory}");
Console.WriteLine("可视化测试：http://127.0.0.1:5177");
Console.WriteLine("按 Ctrl+C 结束。");
Console.WriteLine();

app.Run();
