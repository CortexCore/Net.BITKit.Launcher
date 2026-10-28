using Net.BITKit.Launcher;
using Spectre.Console;

namespace Net.BITKit.Launcher.WinForm;

internal static class LauncherApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.FirstOrDefault()?.ToLowerInvariant() is "menu" or "cli")
        {
            args = PromptForCommand();
            if (args.Length == 0)
            {
                return LauncherExitCodes.Success;
            }
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            var options = ParseOptions(args);
            if (options.ShowHelp)
            {
                WriteUsage();
                return LauncherExitCodes.Success;
            }

            var manifestService = new ManifestService();
            if (options.Generate)
            {
                var root = Path.GetFullPath(options.Directory ?? Environment.CurrentDirectory);
                if (!Directory.Exists(root))
                {
                    throw new DirectoryNotFoundException($"目录不存在: {root}");
                }

                using var instanceLock = SingleInstanceLock.TryAcquire(root);
                if (instanceLock is null)
                {
                    return WriteAlreadyRunning();
                }

                AnsiConsole.MarkupLine($"生成清单: [blue]{Markup.Escape(root)}[/]");
                var manifest = await manifestService.GenerateAsync(root, cancellationToken: cancellation.Token);
                if (!string.IsNullOrWhiteSpace(options.Name))
                {
                    manifest.Name = options.Name;
                }
                if (!string.IsNullOrWhiteSpace(options.BaseUrl))
                {
                    manifest.BaseUrl = options.BaseUrl.TrimEnd('/');
                }
                if (!string.IsNullOrWhiteSpace(options.Launcher))
                {
                    manifest.Launcher = options.Launcher;
                }

                await manifestService.WriteAtomicAsync(
                    Path.Combine(root, ManifestService.FileName),
                    manifest,
                    cancellation.Token);
                if (!string.IsNullOrWhiteSpace(options.FileList))
                {
                    var fileListPath = Path.GetFullPath(options.FileList);
                    Directory.CreateDirectory(Path.GetDirectoryName(fileListPath)!);
                    await File.WriteAllLinesAsync(
                        fileListPath,
                        manifest.Files.Select(file => file.Path),
                        new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                        cancellation.Token);
                    AnsiConsole.MarkupLine($"发布文件列表: [blue]{Markup.Escape(fileListPath)}[/]");
                }

                Manifest? previousManifest = null;
                if (!string.IsNullOrWhiteSpace(options.PreviousManifest))
                {
                    var previousManifestPath = Path.GetFullPath(options.PreviousManifest);
                    previousManifest = await manifestService.LoadAsync(previousManifestPath, cancellation.Token);
                }

                var diff = ManifestDiffService.Compare(
                    manifest,
                    previousManifest,
                    options.PreviousRoot,
                    options.ForceAll);
                if (!string.IsNullOrWhiteSpace(options.ChangedFileList))
                {
                    var changedFileListPath = Path.GetFullPath(options.ChangedFileList);
                    Directory.CreateDirectory(Path.GetDirectoryName(changedFileListPath)!);
                    await File.WriteAllLinesAsync(
                        changedFileListPath,
                        diff.Uploads.Select(change => change.Entry.Path),
                        new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                        cancellation.Token);
                    AnsiConsole.MarkupLine($"待上传文件列表: [blue]{Markup.Escape(changedFileListPath)}[/]");
                }

                if (options.Preview)
                {
                    AnsiConsole.WriteLine();
                    foreach (var line in ManifestDiffService.CreatePreview(diff))
                    {
                        AnsiConsole.WriteLine(line);
                    }
                }

                AnsiConsole.MarkupLine($"[green]清单生成完成[/]，共 {manifest.Files.Count} 个文件，算法 {manifest.HashAlgorithm}。");
                return LauncherExitCodes.Success;
            }

            var workspace = Path.GetFullPath(Environment.CurrentDirectory);
            using var updateLock = SingleInstanceLock.TryAcquire(workspace);
            if (updateLock is null)
            {
                return WriteAlreadyRunning();
            }

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var updateService = new UpdateService(
                manifestService,
                httpClient,
                message => AnsiConsole.MarkupLine(Markup.Escape(message)));
            var plan = await updateService.BuildPlanAsync(workspace, cancellation.Token);

            WritePlan(plan, options.DryRun);
            if (options.DryRun)
            {
                AnsiConsole.MarkupLine("[yellow]模拟完成：未下载、未写文件、未启动应用。[/]");
                return LauncherExitCodes.Success;
            }

            var blockers = RestartManager.GetBlockingProcesses(RestartManager.GetChangedPaths(plan));
            if (blockers.Count > 0)
            {
                WriteBlockingProcesses(blockers);
                return LauncherExitCodes.FilesInUse;
            }

            var result = await updateService.ApplyAsync(
                workspace,
                plan,
                options.MaxConcurrency,
                options.LaunchAfterUpdate,
                cancellation.Token);

            if (result.SelfUpdateScheduled)
            {
                AnsiConsole.MarkupLine("[green]新版启动器已校验；临时更新进程将在本进程退出后完成替换。[/]");
                return LauncherExitCodes.Success;
            }

            AnsiConsole.MarkupLine($"[green]更新完成[/]：下载 {result.DownloadedCount}，删除 {plan.Deletions.Count}，跳过 {plan.SkippedCount}。");
            if (options.LaunchAfterUpdate && !string.IsNullOrWhiteSpace(plan.RemoteManifest.Launcher))
            {
                AnsiConsole.MarkupLine($"启动应用: [blue]{Markup.Escape(plan.RemoteManifest.Launcher)}[/]");
                ProcessLauncher.Launch(
                    workspace,
                    plan.RemoteManifest.Launcher,
                    plan.RemoteManifest.GameArguments,
                    plan.RemoteManifest.GameEnvironment);
            }

            return LauncherExitCodes.Success;
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]操作已取消。[/]");
            return LauncherExitCodes.Cancelled;
        }
        catch (ArgumentException exception)
        {
            AnsiConsole.MarkupLine($"[red]参数错误：[/]{Markup.Escape(exception.Message)}");
            WriteUsage();
            return LauncherExitCodes.InvalidArguments;
        }
        catch (Exception exception)
        {
            AnsiConsole.MarkupLine($"[red]更新失败：[/]{Markup.Escape(exception.Message)}");
#if DEBUG
            AnsiConsole.WriteException(exception);
#endif
            return LauncherExitCodes.Failure;
        }
    }

    private static int WriteAlreadyRunning()
    {
        AnsiConsole.MarkupLine("[yellow]此目录中的启动器已经在运行。[/]");
        return LauncherExitCodes.AlreadyRunning;
    }

    private static void WriteBlockingProcesses(IReadOnlyList<BlockingProcess> blockers)
    {
        AnsiConsole.MarkupLine("[yellow]以下程序正在使用待更新文件，请先关闭后重试：[/]");
        var table = new Table().AddColumn("程序").AddColumn("PID").AddColumn("可重启");
        foreach (var blocker in blockers)
        {
            table.AddRow(
                Markup.Escape(blocker.Name),
                blocker.ProcessId.ToString(),
                blocker.Restartable ? "是" : "否");
        }

        AnsiConsole.Write(table);
    }

    private static Options ParseOptions(string[] args)
    {
        if (args.Any(argument => argument.ToLowerInvariant() is "-h" or "-help" or "--help" or "/?" or "help"))
        {
            return new Options { ShowHelp = true };
        }

        var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "update";
        var isGenerate = command is "generate" or "gen" or "g";
        var isExplicitUpdate = command is "update" or "up";
        var options = new Options { Generate = isGenerate };

        for (var index = isGenerate || isExplicitUpdate ? 1 : 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument.ToLowerInvariant())
            {
                case "--dry-run":
                case "--simulate":
                    options.DryRun = true;
                    break;
                case "--no-launch":
                    options.LaunchAfterUpdate = false;
                    break;
                case "--concurrency" when index + 1 < args.Length:
                    options.MaxConcurrency = ParseConcurrency(args[++index]);
                    break;
                case "--file-list" when index + 1 < args.Length:
                    options.FileList = args[++index];
                    break;
                case "--changed-file-list" when index + 1 < args.Length:
                    options.ChangedFileList = args[++index];
                    break;
                case "--previous-manifest" when index + 1 < args.Length:
                    options.PreviousManifest = args[++index];
                    break;
                case "--previous-root" when index + 1 < args.Length:
                    options.PreviousRoot = args[++index];
                    break;
                case "--preview":
                    options.Preview = true;
                    break;
                case "--name" when index + 1 < args.Length:
                    options.Name = args[++index];
                    break;
                case "--base-url" when index + 1 < args.Length:
                    options.BaseUrl = args[++index];
                    break;
                case "--launcher" when index + 1 < args.Length:
                    options.Launcher = args[++index];
                    break;
                case "--force-all":
                    options.ForceAll = true;
                    break;
                default:
                    if (argument.StartsWith("--concurrency=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.MaxConcurrency = ParseConcurrency(argument[(argument.IndexOf('=') + 1)..]);
                    }
                    else if (argument.StartsWith("--file-list=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.FileList = argument[(argument.IndexOf('=') + 1)..];
                    }
                    else if (options.Generate && options.Directory is null && !argument.StartsWith('-'))
                    {
                        options.Directory = argument;
                    }
                    else
                    {
                        throw new ArgumentException($"未知参数: {argument}");
                    }

                    break;
            }
        }

        if (options.Generate && options.DryRun)
        {
            throw new ArgumentException("generate 命令不支持 --dry-run。");
        }

        if (!options.Generate && options.HasGenerateOutputOptions)
        {
            throw new ArgumentException("清单生成参数只能与 generate 命令一起使用。");
        }

        if (!string.IsNullOrWhiteSpace(options.PreviousRoot)
            && string.IsNullOrWhiteSpace(options.PreviousManifest))
        {
            throw new ArgumentException("--previous-root 必须与 --previous-manifest 一起使用。");
        }

        return options;
    }

    private static int ParseConcurrency(string value) =>
        int.TryParse(value, out var concurrency) && concurrency is >= 1 and <= 32
            ? concurrency
            : throw new ArgumentOutOfRangeException(nameof(value), "并发数必须在 1 到 32 之间。");

    private static void WritePlan(UpdatePlan plan, bool dryRun)
    {
        var title = dryRun ? "模拟更新计划" : "更新计划";
        AnsiConsole.MarkupLine($"[bold]{title}[/]：下载 {plan.Downloads.Count}，删除 {plan.Deletions.Count}，跳过 {plan.SkippedCount}。");
        foreach (var file in plan.Downloads)
        {
            var marker = file.IsSelfUpdate ? " [yellow](启动器自更新)[/]" : "";
            AnsiConsole.MarkupLine($"  [green]+[/] {Markup.Escape(file.Entry.Path)}{marker}");
        }

        foreach (var path in plan.Deletions)
        {
            AnsiConsole.MarkupLine($"  [red]-[/] {Markup.Escape(Path.GetRelativePath(Environment.CurrentDirectory, path))}");
        }
    }

    private static void WriteUsage()
    {
        var executable = Path.GetFileName(Environment.ProcessPath) ?? "Net.BITKit.Launcher";
        AnsiConsole.MarkupLine("[bold]Net.BITKit.Launcher[/] - 差量更新工具");
        AnsiConsole.MarkupLine($"当前程序：[blue]{Markup.Escape(executable)}[/]");
        var table = new Table().AddColumn("命令").AddColumn("说明");
        table.AddRow("launcher", "打开图形更新界面");
        table.AddRow("launcher update", "使用 CLI 执行更新（别名 up）");
        table.AddRow("launcher update --dry-run", "只计算更新计划，不修改文件");
        table.AddRow("launcher --no-launch", "更新后不启动应用");
        table.AddRow("launcher --concurrency 4", "设置并发下载数（1-32）");
        table.AddRow(Markup.Escape("launcher generate [目录]"), "生成 SHA-256 清单（别名 gen、g）");
        table.AddRow("launcher generate --file-list FILE", "生成清单并输出发布文件列表");
        table.AddRow("launcher generate --previous-manifest FILE", "与上一版清单比较");
        table.AddRow("launcher generate --previous-root DIR", "检查上一版服务器文件是否存在");
        table.AddRow("launcher generate --preview", "在终端输出待上传文件树和摘要");
        table.AddRow("launcher generate --changed-file-list FILE", "输出待上传文件列表");
        table.AddRow("launcher generate --force-all", "将全部文件列为待上传");
        table.AddRow("launcher menu", "打开终端交互菜单（别名 cli）");
        table.AddRow("launcher --ui-preview", "打开离线界面预览（禁用更新与启动）");
        table.AddRow("launcher --simulate-install", "模拟安装检查，不写文件、不创建快捷方式");
        table.AddRow("launcher --help", "显示本帮助（也支持 -help、-h、/?、help）");
        AnsiConsole.Write(table);
    }

    private static string[] PromptForCommand()
    {
        const string Update = "更新并启动应用";
        const string Simulate = "模拟更新（不写文件）";
        const string GenerateCurrent = "生成当前目录清单";
        const string GenerateOther = "生成指定目录清单";
        const string Exit = "退出";

        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("请选择要执行的操作：")
                .AddChoices(Update, Simulate, GenerateCurrent, GenerateOther, Exit));

        return selection switch
        {
            Update => ["update"],
            Simulate => ["update", "--dry-run"],
            GenerateCurrent => ["generate"],
            GenerateOther => ["generate", AnsiConsole.Prompt(new TextPrompt<string>("请输入目标目录："))],
            _ => []
        };
    }

    private sealed class Options
    {
        public bool Generate { get; init; }
        public string? Directory { get; set; }
        public string? FileList { get; set; }
        public string? ChangedFileList { get; set; }
        public string? PreviousManifest { get; set; }
        public string? PreviousRoot { get; set; }
        public bool Preview { get; set; }
        public string? Name { get; set; }
        public string? BaseUrl { get; set; }
        public string? Launcher { get; set; }
        public bool ForceAll { get; set; }
        public bool DryRun { get; set; }
        public bool LaunchAfterUpdate { get; set; } = true;
        public int MaxConcurrency { get; set; } = 4;
        public bool ShowHelp { get; init; }

        public bool HasGenerateOutputOptions =>
            !string.IsNullOrWhiteSpace(FileList)
            || !string.IsNullOrWhiteSpace(ChangedFileList)
            || !string.IsNullOrWhiteSpace(PreviousManifest)
            || !string.IsNullOrWhiteSpace(PreviousRoot)
            || Preview
            || !string.IsNullOrWhiteSpace(Name)
            || !string.IsNullOrWhiteSpace(BaseUrl)
            || !string.IsNullOrWhiteSpace(Launcher)
            || ForceAll;
    }
}
