using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Net.BITKit.Launcher;
using Spectre.Console;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

AnsiConsole.WriteLine("Net.BITKit.Launcher - 版本更新工具");

Console.WriteLine("使用方式:");

var table = new Table();

// 添加列
table.AddColumn("命令");
table.AddColumn("说明");

// 添加行
table.AddRow("./Net.BITKit.Launcher.exe", "默认下载或更新");
table.AddRow("./Net.BITKit.Launcher.exe generate", "生成清单");
table.AddRow("./Net.BITKit.Launcher.exe generate <目录>", "生成指定目录的清单");

// 输出表格
AnsiConsole.Write(table);

Console.WriteLine($"当前工作空间:{Environment.CurrentDirectory}");
    
    Console.WriteLine();

var deserializer = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .IgnoreUnmatchedProperties()  // 忽略 YAML 中 Manifest 类没有的字段
    .Build();

var serializer = new SerializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .Build();


//从.gitignore检测忽略文件
var ignore = new  Ignore.Ignore();

var gitignorePath= Path.Combine(Environment.CurrentDirectory, ".gitignore");
if (File.Exists(gitignorePath))
{
    Console.WriteLine($"检测到 .gitignore 文件: {gitignorePath}");
    ignore.Add(File.ReadLines(gitignorePath));
}

// ===== 参数判断 =====


if (args.Length > 0 && args[0].Equals("generate", StringComparison.OrdinalIgnoreCase))
{
    var dir = args.Length > 1 ? args[1] : Directory.GetCurrentDirectory();

    if (Directory.Exists(dir) is false)
    {
        Console.WriteLine($"目录不存在: {dir}");
        
        return;
    }

    Console.WriteLine($"生成清单: {dir}");

    await GenerateManifest(dir);

    Console.WriteLine("清单生成完成.");
    return;
}

    await DownloadManifestFilesAsync();

// 默认：更新流程


// ===== 方法 =====

async Task GenerateManifest(string root)
{
    var manifest = new Manifest
    {
        Version = DateTime.UtcNow.ToString("yyyy.MM.dd.HHmmss"),
        Name = Path.GetDirectoryName(root)!,
    };

    var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);

    await AnsiConsole.Progress()
        .StartAsync(async ctx =>
        {
            var task = ctx.AddTask("[yellow]扫描文件...[/]", maxValue: files.Length);

            foreach (var file in files)
            {
                task.Increment(1);
                
                var relativePath = Path.GetRelativePath(
                    Path.GetDirectoryName(gitignorePath)!,
                    file
                ).Replace("\\", "/"); // git 用 /

                if (ignore.IsIgnored(relativePath))
                {
                    continue;
                }
                
                var relative = Path.GetRelativePath(root, file).Replace("\\", "/");
                var info = new FileInfo(file);

                manifest.Files.Add(new FileEntry
                {
                    Path = relative,
                    Size = info.Length,
                    Hash = await CalcHash(file)
                });
            }
        });


    var outputPath = Path.Combine(root, "manifest.yaml");
    //计算现有的mainfest,集成baseUrl和Name

    if (File.Exists(outputPath))
    {
        try
        {
            var existManifest = deserializer.Deserialize<Manifest>(await File.ReadAllTextAsync(outputPath));
            
            manifest.BaseUrl = existManifest.BaseUrl;
            manifest.Name = existManifest.Name;
            manifest.Launcher = existManifest.Launcher;
        }
        catch (Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"反序列化现有清单错误:\n{e}");
            Console.ForegroundColor = ConsoleColor.White;
        }
    }

    var yaml = serializer.Serialize(manifest);

    deserializer.Deserialize<Manifest>(yaml);

    await File.WriteAllTextAsync(outputPath, yaml);
}

static async Task<string> CalcHash(string file)
{
    using var md5 = MD5.Create();
    await using var stream = File.OpenRead(file);

    var hash = await md5.ComputeHashAsync(stream);
    return Convert.ToHexString(hash);
}

async Task DownloadManifestFilesAsync(int maxConcurrency = 4)
{
    var manifestPath = Path.Combine(Directory.GetCurrentDirectory(), "manifest.yaml");

    if (!File.Exists(manifestPath))
    {
        Console.WriteLine("清单文件不存在，请先生成清单。");
        return;
    }

    var yaml = File.ReadAllText(manifestPath);

    var manifest = deserializer.Deserialize<Manifest>(yaml);

    var baseUrl = manifest.BaseUrl;

    if (string.IsNullOrEmpty(baseUrl))
    {
        throw new Exception("清单文件中未指定 BaseUrl，请检查 manifest.yaml 文件。");
    }

    var client = new HttpClient();

    var manifestUrl = $"{manifest.BaseUrl}/manifest.yaml";

    yaml = await client.GetStringAsync(manifestUrl);

    try
    {
        manifest = deserializer.Deserialize<Manifest>(yaml);
        manifest.BaseUrl = baseUrl;
    }
    catch (Exception e)
    {
        Console.WriteLine("反序列化错误:");
        Console.WriteLine(yaml);
        throw;
    }


    var files = manifest.Files;

    if (files.Count is 0)
    {
        throw new Exception($"清单文件中没有文件信息，请检查 manifest.yaml 文件。\n{yaml}");
    }


    try
    {
        await AnsiConsole.Progress()
            .StartAsync(Download);
    
        Console.WriteLine("所有文件下载完成.");
    }
    catch (Exception e)
    {
        Console.WriteLine("更新错误,按任意键退出");
        
        Console.WriteLine(e);

        Console.ReadKey();        
        #if DEBUG
        throw;
        #endif
        return;
    }
  

    if (manifest.Launcher is { Length: > 0 })
    {
        var launcherPath= Path.Combine(Environment.CurrentDirectory, manifest.Launcher);
        
        if (File.Exists(launcherPath))
        {
            Console.WriteLine($"正在启动应用: {manifest.Launcher}");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = launcherPath,
                UseShellExecute = true
            });
        }
        else
        {
            Console.WriteLine($"启动文件不存在: {launcherPath}");
        }
    }
    else
    {
        Console.WriteLine("清单中未指定启动文件，请检查 manifest.yaml 文件。");
    }
    
    return;

    async Task Download(ProgressContext ctx)
    {
        var buffer = new byte[81920]; // 80 KB
        
        string exeName = Path.GetFileName(Process.GetCurrentProcess().MainModule!.FileName);
        
        var totalSize = files.Sum(f => f.Size);
        long currentSize = 0;
        
        var fileTask = ctx.AddTask("[yellow]下载文件...[/]", maxValue: files.Count, autoStart: false);

        var consecutiveError = 0;

        for (var i = 0; i < files.Count; i++)
        {
            var f = files[i];
            var t = ctx.AddTask(f.Path);
            try
            {
                var url = $"{manifest.BaseUrl}/{f.Path}";
                var localPath = Path.Combine(Environment.CurrentDirectory, f.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

                //先判断现有文件的md5是否相同
                if (File.Exists(localPath))
                {
                    using var md5 = MD5.Create();
                    await using var fs2 = File.OpenRead(localPath);
                    var hash = Convert.ToHexString(await md5.ComputeHashAsync(fs2));
                    if (hash.Equals(f.Hash, StringComparison.OrdinalIgnoreCase))
                    {
                        t.Description($"已存在: {f.Path}");
                        totalSize -= f.Size;
                        continue;
                    }
                }
                
            
                {  
                    await using var resp = await client.GetStreamAsync(url);
                    var totalBytesRead = 0L;

                    int bytesRead;

                    var path = localPath;

                    var isLauncher = f.Path.Equals(exeName, StringComparison.OrdinalIgnoreCase);
                    
                    if (isLauncher)
                    {
                        t.Description = "[green]暂未支持启动器自更新,保存到.new[/]";
                        
                        path = Path.GetFileNameWithoutExtension(localPath) + ".new" + Path.GetExtension(localPath);
                    }

                    await using var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);

                    while ((bytesRead = await resp.ReadAsync(buffer)) > 0)
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, bytesRead));
                        totalBytesRead += bytesRead;

                        t.Description($"{f.Path}@{totalBytesRead / 1024}KB");
                        t.Value = ((double)totalBytesRead / f.Size);
                    }

                    if (isLauncher)
                    {
                        DownloadSucceed();
                        continue;
                    }
                }

                //检测路径是否是本exe路径
                

                if (f.Path.Equals("manifest.yaml", StringComparison.OrdinalIgnoreCase))
                {
                    t.Description = "[green]已更新资源清单[/]";
                }
                else
                {
                    // 校验 Hash
                    using var md5 = MD5.Create();
                    await using var fs2 = File.OpenRead(localPath);
                    var hash = Convert.ToHexString(await md5.ComputeHashAsync(fs2));
                    if (!hash.Equals(f.Hash, StringComparison.OrdinalIgnoreCase))
                    {
                        t.Description($"[yellow]下载可能损坏: {f.Path}[/]");
                        continue;
                    }
                }

                DownloadSucceed();
            }
            catch (Exception e)
            {
                consecutiveError++;

                t.Description = $"[red]错误:[/]:{e.Message}";

                if (consecutiveError >= 3)
                {
                    throw new ExternalException($"连续 {consecutiveError} 个文件下载失败，终止更新。", e);
                }
            }
            finally
            {
                t.Value = t.MaxValue;

                fileTask.Increment(1);

                fileTask.Description = $"下载中::{i+1}/{files.Count} 已下载:({currentSize/1024}/{totalSize/1024} KB)";
            }

            continue;
            void DownloadSucceed()
            {
                currentSize += f.Size;

                consecutiveError = 0;
            }
            
        }
        
    }
}


