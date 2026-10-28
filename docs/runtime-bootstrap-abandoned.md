# Runtime Bootstrap 方案放弃记录

## 决策

仓库停止并放弃“轻量永久入口 EXE 自动补齐 .NET 10 Desktop Runtime 和 WebView2”的实现方向。

实验性的 `Net.BITKit.Launcher.Bootstrap` 项目、`LauncherHost` 边界和环境变量协议已从代码中移除。生产方案恢复为百余 MiB 的 .NET 10 `win-x64` 自包含单文件；它无需预装 .NET，当前原生 WPF 界面也不依赖 WebView2 Runtime。

## 验证结果

该方向先后进行了两次长时间方案验证，两次均失败。

### 第一次长时间验证：仓库内 Bootstrap PoC

第一次验证实现了 .NET Framework 4.8 外壳，将 framework-dependent .NET 10 托管载荷嵌入单个 EXE，并验证了载荷释放、Runtime 下载、Microsoft Authenticode 校验、启动和自更新链路。

该方案在开发机上能够运行，但最终验证失败，原因是外壳本身仍依赖 .NET Framework 4.8。目标用户可能使用精简版或修改版 Windows，不能假设该组件存在，也不能用一个可能缺失的托管运行时去引导另一个缺失的托管运行时。因此该 PoC 不满足最初的可靠性前提。

### 第二次长时间验证：现有方案与上游项目调查

第二次验证对现有方案进行了源码、文档和隔离构建检查，包括 DotnetRuntimeBootstrapper、Velopack、官方 .NET AppHost、WebView2 官方部署方式以及传统安装器方案。

验证仍然失败，没有找到同时满足以下条件的成熟方案：

- 玩家只接触并始终运行同一个 Launcher EXE。
- 外壳不依赖任何预装 .NET Framework 或 .NET Runtime。
- 自动安装 .NET 10 Desktop Runtime。
- 自动安装 WebView2 Evergreen Runtime。
- 对下载的安装程序执行可靠的签名或发布哈希校验。
- 与仓库现有的原地自更新模型兼容。

具体阻塞如下：

- DotnetRuntimeBootstrapper 的当前发布版可以用于 `net10.0-windows` 的普通 framework-dependent 发布，但外壳目标为 `net35`，不支持 WebView2，也明确不支持 single-file。隔离测试启用 `PublishSingleFile=true` 后以 `MSB4018 BootstrapperTask` 失败。
- Velopack 支持 .NET Desktop Runtime 和 WebView2 前置依赖，但其模型是 `Setup.exe`、安装后的主程序和 `Update.exe` 分离，不是同一个永久入口 EXE。
- WiX Burn、Inno Setup 等传统方案同样区分安装器与安装后的应用。
- 官方 .NET AppHost 能查找并启动已安装 Runtime，但不会自动下载和安装缺失 Runtime。
- 严格满足全部条件需要新的静态原生外壳；当前构建环境没有已批准的 C/C++ 或其他原生工具链，并且项目决定不再继续投入该方向。

## 后续约束

- 不继续修补 .NET Framework 4.8 Bootstrap PoC。
- Project B 自建发布脚本只允许直接生成自包含单文件，不得重新接入已删除的 Runtime 外壳。
- 不安装新原生工具链，也不再为该方向开展第三轮实现验证，除非未来作出新的明确决策。
- 若未来重新启动 Runtime 引导工作，必须建立新的设计决策，不能把本记录中的 PoC 直接恢复为生产方案。
- 清单差异、下载事务、自更新和原生 UI 等与 Runtime 外壳无关的代码仍可独立复用。
