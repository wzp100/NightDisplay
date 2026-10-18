# 贡献指南

先阅读 README 与架构说明。开发要求 Windows x64、MSVC x64 与 Windows SDK（Visual Studio C++ Build Tools），中文文件使用 UTF-8；PowerShell 脚本使用 UTF-8 BOM 兼容 Windows PowerShell 5.1。PowerShell `Get-Content` 必须显式指定 `-Encoding utf8`，不要使用 `-Raw`。

提交前执行 `./build.ps1 -Test` 及 `dotnet build NightDisplay.sln -c Release`。CI 会执行相同检查。普通集成检查不实际切换显示器。涉及显示切换、心跳、守护进程或恢复记录格式的修改必须另做实机回归，并记录系统版本、显卡、显示器拓扑与恢复结果；不要在无人看护的机器上直接做首次故障测试。

显示恢复配置与访问密钥存储在用户 AppData，格式升级须兼容已有配置。修改网络服务须保持私有来源、Host/Origin、访问密钥、固定资源路由、请求大小和总读取时限的约束。

保持后台与界面进程分离、Win32 守护和无前端依赖的方案。后台不得引用 WPF 类型、Theme 或创建设置窗口；网页关闭时不得创建监听器。修改共享内存布局必须同时更新 C#、C++ 和检查中的协议版本。新增后台计时器、磁盘轮询或第三方运行时前，应说明实际需求及资源开销。界面不使用持续动画，字体和图标复用。界面样式集中在 UI/Theme.xaml，颜色只通过 Theme.cs 的画刷键引用，以便深浅色切换。

PR 描述应解释具体问题、修改后的行为、验证结果与尚未覆盖的硬件情况。请勿提交本机 EXE、快捷方式、二维码、访问链接、AppData 配置或诊断日志。
