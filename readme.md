# Net.BITKit.Launcher

一个基于 .NET 8 的文件清单更新器。启动器从远端取得 `manifest.yaml`，比较本地文件哈希，只下载发生变化的文件，更新完成后启动目标应用。

## 命令

```powershell
# 双击或无参数运行：打开图形更新界面
./Net.BITKit.Launcher.exe

# 在终端直接执行更新
./Net.BITKit.Launcher.exe update

# 忘记命令时显示帮助，或打开交互菜单
./Net.BITKit.Launcher.exe --help
./Net.BITKit.Launcher.exe menu

# 只计算计划，不下载、不写文件、不启动应用
./Net.BITKit.Launcher.exe --dry-run
./Net.BITKit.Launcher.exe --simulate

# 更新后不启动应用
./Net.BITKit.Launcher.exe --no-launch

# 设置并发下载数（1 到 32，默认 4）
./Net.BITKit.Launcher.exe --concurrency 8

# 生成当前目录或指定目录的清单
./Net.BITKit.Launcher.exe generate
./Net.BITKit.Launcher.exe generate <目录>

# 同时输出清单中的精确文件列表，供发布脚本使用
./Net.BITKit.Launcher.exe generate <目录> --file-list files.txt
```

帮助命令同时支持 `--help`、`-help`、`-h`、`/?` 和 `help`。`update` 可简写为 `up`，`generate` 可简写为 `gen` 或 `g`，交互菜单 `menu` 可写成 `cli`。无参数运行打开 WinForms 界面；传入任何更新参数时走 CLI，便于脚本调用。

## manifest.yaml

首次生成后至少需要配置：

```yaml
name: MyApp
baseUrl: https://example.com/my-app
launcher: MyApp.exe
announcement:
  title: 维护公告
  content: 本次更新修复了若干问题。
  url: https://example.com/news
ui:
  preset: gameHero
  background: launcher/background.jpg
  accentColor: "#F8C63D"
  buttonText: 启动游戏
```

- `baseUrl`：静态文件服务器目录。启动器会从该目录读取 `manifest.yaml` 和文件。
- `launcher`：更新完成后启动的相对路径，不能越出应用目录。
- `gameArguments`：可选的游戏启动参数字符串。
- `gameEnvironment`：可选的游戏进程环境变量映射；变量值中的 `%VAR%` 会在启动前展开。
- `hashAlgorithm`：新清单使用 `SHA256`；旧版未声明算法的 MD5 清单仍然兼容。
- `files[].url`：可选的独立下载地址。
- `files[].required`：设为 `false` 时，单个可选文件下载失败不会终止整次更新。
- `announcement`：可选。图形界面显示标题、正文和 HTTP/HTTPS 详情链接。
- `ui.preset`：默认 `gameHero`；设为 `compact` 可使用紧凑工具界面。
- `ui.background`：可选的游戏背景相对路径。图片会载入内存后释放文件句柄，可由更新器正常替换；省略时使用 EXE 内置背景。
- `ui.accentColor`：主按钮、进度条和链接使用的 HTML 颜色。
- `ui.buttonText`：游戏已是最新版本时主按钮显示的文字。

生成清单时会读取目标目录自己的 `.gitignore`。`.gitignore`、`manifest.yaml` 和 `.bitkit-update` 工作目录不会被加入清单。

## Like Tokyo OSS 发布

仓库内提供了专用脚本 `tools/publish-like-tokyo.ps1`，默认指向 Like Tokyo V6 游戏目录、`like-game` OSS Bucket 和当前 `ossutil` 安装位置。

```powershell
# 推荐先跑：完整计算与 OSS 差异预览，不修改游戏目录和 OSS
powershell -ExecutionPolicy Bypass -File tools/publish-like-tokyo.ps1 -DryRun

# 正式发布；脚本会要求手动输入 PUBLISH
powershell -ExecutionPolicy Bypass -File tools/publish-like-tokyo.ps1
```

脚本会把 PNG 转换为包含 16 到 256 像素图层的 ICO，构建带专用图标的单文件启动器，生成 SHA-256 清单，并按清单建立 NTFS 硬链接发布区。随后只启动一次 `ossutil sync --checksum` 批量同步资源，全部成功后才单独上传 `manifest.yaml`，避免客户端下载到尚未完整上传的新版本。

脚本不会使用 `--delete`：旧的无引用对象留在 OSS 不影响客户端，也避免误删和发布窗口中断。确认发布稳定后，可另做受控的远端垃圾清理，不应把删除混进常规发布。

`ossutil.exe` 可以继续放在 `D:\Iris\Applications\ossutil-2`。需要迁移时设置 `OSSUTIL_PATH`，或传入 `-OssUtilPath`；无需让终端用户安装 ossutil 或接触 OSS 凭据。

## 迷你安装器与配置目录

带有产品启动信息的单文件启动器也可以直接作为安装器分发。第一次从下载目录运行时，它会推荐一个可写且空间充足的游戏目录；安装时复制启动器、写入安装记录并可创建桌面和开始菜单快捷方式。原始下载文件不会被移动或删除，之后再次运行它会跳转到已安装的启动器。

安装器会在允许继续前验证路径格式、非法字符、磁盘根目录、目标是否误选为文件、目录写权限和可用空间；同时会单独诊断 manifest 地址的 HTTP 状态、连接/DNS/TLS 失败和请求超时。

启动器自己的安装记录默认位于 `%LOCALAPPDATA%\BITKit\Launcher`。可以通过环境变量或参数迁移：

```powershell
$env:BITKIT_LAUNCHER_HOME = 'D:\BITKitData'
./Net.BITKit.Launcher.exe --config-dir 'D:\BITKitData'
```

Windows Unity Player 默认把 `Application.persistentDataPath` 放在 `%USERPROFILE%\AppData\LocalLow\<CompanyName>\<ProductName>`。Like Tokyo 当前路径为 `%USERPROFILE%\AppData\LocalLow\LikeXue\LikeTokyo`。启动器不会假设一个不存在的 Unity 覆盖开关；如游戏侧提供了自定义参数，可在清单中配置：

```yaml
gameArguments: '-my-option value'
gameEnvironment:
  LIKE_TOKYO_DATA_DIR: '%USERPROFILE%\AppData\LocalLow\LikeXue\LikeTokyo'
```

这些参数和环境变量只传给游戏进程，不传给启动器自身。Unity 官方说明：Windows Player 的 `persistentDataPath` 是按公司名和产品名解析的固定路径；是否支持自定义目录取决于游戏代码是否主动读取参数。[Unity persistentDataPath](https://docs.unity3d.com/cn/2023.2/ScriptReference/Application-persistentDataPath.html)

## 更新安全性

- 所有远端路径都必须位于应用目录内，拒绝绝对路径、`..`、符号链接和目录联接穿越。
- 文件先下载到隔离目录，大小和哈希全部通过后才替换正式文件。
- 应用阶段保留临时备份；中途失败会回滚已经替换的文件。
- 远端清单删除的旧文件，只有在用户没有修改该文件时才会删除。
- 同一安装目录只允许一个 GUI、更新或清单生成任务运行；不同安装目录互不影响。
- 应用更新前通过 Windows Restart Manager 检查待替换/删除文件，若被其他进程占用则列出程序名和 PID，不会强制结束用户程序。

SHA-256 只能验证下载内容是否与清单一致。若需要抵御服务器或清单被篡改，还应增加清单数字签名。

## 启动器自更新

Windows 不允许普通程序删除或覆盖仍在运行、仍被内存映射的 EXE。启动器发现自身需要更新时会：

1. 校验并暂存新版启动器。
2. 将当前启动器及其运行依赖复制到系统临时目录，启动一次性辅助进程。
3. 主进程退出后，辅助进程替换正式启动器并启动新版。
4. 新版删除临时辅助目录和回滚文件，再启动目标应用。

因此安装目录不需要长期保留两个轮换的 EXE。发布 Windows 版本时建议使用单文件模式：

```powershell
dotnet publish Net.BITKit.Launcher.WinForm/Net.BITKit.Launcher.WinForm.csproj `
  -c Release -r win-x64 --self-contained true
```

项目在指定 `RuntimeIdentifier` 时默认启用 `PublishSingleFile`。

## 替换 EXE 图标

把 Windows ICO 文件放到：

```text
Net.BITKit.Launcher.WinForm/Assets/launcher.ico
```

下次构建或发布时会自动将图标编入 EXE。推荐 ICO 至少包含 16、32、48、64、128、256 像素图层。不要在程序运行时修改 EXE 的 PE 图标资源；这样会破坏代码签名，也容易触发安全软件。

## 项目结构

- `Net.BITKit.Launcher`：与界面无关的清单、下载、事务替换和自更新核心库。
- `Net.BITKit.Launcher.WinForm`：生成最终 `Net.BITKit.Launcher.exe`，同时提供 GUI 与 CLI。
