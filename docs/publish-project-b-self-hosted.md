# 发布 Project B 到自建服务器

> [!NOTE]
> 当前发布器生成百余 MiB 的 .NET 10 `win-x64` 自包含单文件。自动下载 Runtime 的轻量外壳方案已经放弃并从代码中移除，历史结论见 [`runtime-bootstrap-abandoned.md`](runtime-bootstrap-abandoned.md)。

`tools/publish-project-b-self-hosted.ps1` 为 Project B 构建独立启动器、生成 SHA-256 文件清单，并把客户端文件发布到普通静态文件服务器的目录。

## 前提

- `D:\Iris\Documents\Workspace\Com.Project.B.Unity` 是待发布的 Windows Player。
- Web 服务器的发布目录可从本机写入，例如本地目录、映射盘或 UNC 共享目录。
- 公网 URL 与服务器目录指向同一位置。
- 服务器允许通过 HTTP/HTTPS `GET` 下载目录中的文件。
- 玩家系统为受支持的 64 位 Windows 10/11；启动器已携带 .NET 10 Runtime，原生 WPF 界面不需要 WebView2 Runtime。

Project B 成品目录中的 `.gitignore` 是发布排除规则。现有清单生成器先按这些 gitignore 风格规则排除 `.unidot`、`Src`、IDE 配置、解决方案和 `Project B_BurstDebugInformation_DoNotShip`，再把剩余文件写入 `manifest.yaml`。服务器只接收清单列出的文件；仓库中的 `tools/project-b.publishignore` 是这份规则的模板。

## 发布流程

Project B 成品目录已经包含 `publish-app.ps1`。先执行模拟，把示例域名和 UNC 路径换成实际值：

在已配置的 BITFALL 工作区中，也可以直接双击 `publish-app.cmd` 打开数字菜单。该 CMD 仅负责绕过 PowerShell 执行策略并传入固定的服务器地址，实际发布逻辑仍由 `publish-app.ps1` 执行。

启动器的 `generate` 命令会在上传前直接在终端显示待上传文件树、总上传大小、文件数、跳过数量和客户端待删除数量。它还会输出机器可读的待上传文件清单，PowerShell 只负责按清单复制文件。预览显示后才会要求输入 `PUBLISH`。

```powershell
Set-Location 'D:\Iris\Documents\Workspace\Com.Project.B.Unity'
.\publish-app.ps1 `
  -DownloadBaseUrl 'https://download.example.com/games/project-b' `
  -DestinationDirectory '\\game-server\wwwroot\games\project-b' `
  -Mode DryRun
```

确认预览后执行正式发布：

```powershell
.\publish-app.ps1 `
  -DownloadBaseUrl 'https://download.example.com/games/project-b' `
  -DestinationDirectory '\\game-server\wwwroot\games\project-b' `
  -Mode Publish
```

输入 `PUBLISH` 后，脚本会：

1. 复制成品目录创建不可变临时发布快照，并应用成品目录中的 `.gitignore`。
2. 构建内置 Project B 产品 ID、安装目录、清单 URL 和图标的自包含单文件启动器。
3. 生成 `manifest.yaml` 和文件 SHA-256，由启动器核心比较服务器旧 `manifest.yaml` 并生成发布预览。
4. PowerShell 读取启动器输出的待上传文件清单，只复制新增、缺失或 size/hash 变化的文件。
5. 所有资源成功后，最后替换 `manifest.yaml`。
6. 从公网 URL 回读清单进行验证，并在成品目录保留同版本启动器和清单。

给玩家分发：

```text
https://download.example.com/games/project-b/Net.BITKit.Launcher.exe
```

该 EXE 就是完整启动器，不会释放或转交给另一层托管载荷。文件较大是自包含 Runtime 的预期结果。

## 直接生成发布预览

开发仓库中可以直接运行托管项目的 `generate` 命令，生成同一份差异结果并在当前终端显示预览：

```powershell
dotnet run --project .\Net.BITKit.Launcher.WinForm\Net.BITKit.Launcher.WinForm.csproj `
  -c Release -- generate 'D:\Build\BITFALL' `
  --name BITFALL `
  --base-url 'https://download.example.com/games/project-b' `
  --launcher 'Project B.exe' `
  --previous-manifest '\\game-server\wwwroot\games\project-b\manifest.yaml' `
  --previous-root '\\game-server\wwwroot\games\project-b' `
  --file-list '.\all-files.txt' `
  --changed-file-list '.\upload-files.txt' `
  --preview
```

`--previous-root` 会额外检查旧清单中未变化的服务器文件是否仍存在且大小正确；缺失或大小错误的文件会标为“补传”。`--force-all` 会把所有现有文件加入上传清单。

## Web 服务器规则

`manifest.yaml` 应禁用缓存；其余大文件可以正常提供静态下载。Nginx 示例：

```nginx
location = /games/project-b/manifest.yaml {
    add_header Cache-Control "no-cache, no-store, must-revalidate";
    try_files $uri =404;
}

location /games/project-b/ {
    try_files $uri =404;
}
```

不要让 Web 服务器返回 SPA 的 `index.html` 作为缺失文件的兜底响应。

## 后续版本

重新构建 Unity Player 到同一个 `GameRoot`，然后再次运行正式发布命令。脚本不会删除服务器上的历史文件，但新清单不再引用的文件会由客户端根据旧清单安全删除。

常用参数：

- `-Force`：跳过 `PUBLISH` 确认。
- `-CopyAll`：不比较服务器文件，强制复制全部资源。
- `-SkipVerification`：不进行公网清单回读，仅适合公网映射尚未启用时。
- `-KeepStaging`：保留临时发布区用于排查。
- `-PublishIgnore <路径>`：改用另一份 gitignore 风格的发布排除规则。

Linux 服务器若没有映射盘或 SMB 共享，需要先把目标目录挂载到 Windows，或再为脚本增加 SSH/rsync 部署方式；不要一次性上传新 `manifest.yaml` 和尚未完成的资源。
