# 架构说明（1.3）

后台、设置窗口与原生守护有独立生命周期。主 EXE 仍采用 .NET Framework 4.8；运行发布包不需要额外下载运行时。

| 进程 | 生命周期 | 职责 |
| --- | --- | --- |
| `NightDisplay.exe` 后台 | 常驻 | 快捷键、显示转换、共享心跳、原生托盘、本地管道；网页按需启动 |
| `NightDisplay.exe --panel` | 打开设置时创建，关闭或最小化时退出 | WPF 界面、主题与图片；通过管道发送操作、读取状态 |
| `NightDisplay.Guard.exe` | 后台启动时创建 | 等待后台退出、共享心跳超时或恢复期限；异常时调用短暂恢复入口 |
| `NightDisplay.exe --restore --headless` | 仅故障或测试恢复期间 | 校验恢复记录所有者，恢复实体屏并退出 |

`AppHost : ApplicationContext` 不创建设置 Form。`HostWindow : NativeWindow` 使用不可见顶层窗口接收全局快捷键与显示广播；WinForms 消息循环仍存在，因此后台仍有 CLR 与基础窗口框架成本。托盘直接调用 `Shell_NotifyIcon`，右键菜单按需创建、随后销毁。后台不加载 WPF 程序集、Theme、设置窗口或桌面预览；Program 只经由不含 WPF 类型签名的 PanelApp.Run 进入界面。退出后台也会关闭设置进程。

`PanelProtocol` 使用当前用户 SID 命名的本机管道，ACL 仅允许当前用户，消息限制 16 KiB，并限制连接等待与读取时间；这不需要 TCP 监听端口。所有显示与设置操作提交到后台消息线程，避免并发转换。关闭界面不会影响快捷键、保护或虚拟输出。

`DisplaySwitchService` 的转换顺序是保存实体屏永久配置 → 原子写恢复记录 → 通知守护 → 按需启用设备并重新枚举 → 验证虚拟配置 → 临时启用虚拟输出 → 验证结果。恢复记录包括设备路径与所有者标识，防止旧守护修改新会话的恢复记录。后台与恢复 helper 通过命名互斥体串行执行显示操作，避免重复启停与恢复记录竞态。

自带 MttVDD 采用设备级按需启停。`VirtualDisplayDevice` 用 SetupAPI 按精确硬件 ID `Root\MttVDD` 枚举设备，即使设备已禁用也会生成可选择的占位项；通过显示适配器接口路径关联设备实例，避免误操作实体显卡或其他虚拟显卡。设置保存稳定的设备实例 ID，进入时先保存恢复记录、启用设备，再重新查询显示路径，不能复用禁用前的适配器 LUID。测试恢复期限从设备准备就绪后开始计算。

`Recovery.RestoreOutput` 先检查活动实体屏，必要时恢复快照，验证成功后才禁用记录中的 MttVDD 设备，并等待显示路径消失。已通过 Win + P 切回实体屏时保留当前布局；禁用失败不会清除恢复标志，守护仍可重试。启动时清理旧版本遗留的已连接虚拟屏。设备已禁用仍保留在设置列表，不触发重复驱动安装。

启停需要管理员权限。安装版在受保护的 `Program Files` 目录中创建两个按设备实例区分的按需系统任务，固定执行 `--device-enable` / `--device-disable`；无定时触发器、无常驻服务。任务 ACL 允许普通用户读取和运行，仅 SYSTEM 和管理员能修改。注册前检查安装目录、程序文件权限和目录链接，拒绝从用户可写目录创建 SYSTEM 任务；运行时核对任务程序与固定参数。设备入口再次按精确硬件 ID 校验，只改变匹配设备状态，不读取用户恢复记录。便携版需以管理员运行，或改用安装版。卸载删除任务。

共享内存协议 v2 固定为 32 字节：偏移 0 是 UTC 心跳 ticks，8 是虚拟标志，16 是恢复期限 ticks，24 是版本号。后台每 3 秒刷新共享心跳；恢复状态仅在转换时写磁盘。原生守护在实体模式无限等待进程/事件，虚拟模式每秒检查 15 秒超时与恢复期限。恢复成功后清除标志；恢复失败后重试。后台失去守护时先恢复实体屏，再重建保护。

守护用 MSVC 构建，不使用 CLR、CRT、STL 或 UI 库，直接依赖 Kernel32，发布 EXE 约 5 KiB。启动时核对后台 PID 与创建时间，避免 PID 重用；恢复 helper 限制为同目录主 EXE、固定命令行且最多一个。helper 只在恢复时加载 .NET，平时不运行。守护的创建属性限制非 Microsoft 二进制加载，避免本机 Windhawk 等组件注入扩大占用；这是该进程自己的创建策略，不修改其他程序或系统设置。[Windows 创建属性说明](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute)。

网页默认关闭；旧设置迁移时要求重新明确开启。关闭状态下 `LanServer` 为 null，不创建 TCP 监听器、HTTP 接收任务或网页资源缓存。开启才创建服务及密钥，关闭会释放监听器与全部活动连接。服务在后台进程内运行，没有额外常驻网页进程；已经加载过的托管模块/静态资源可由 CLR 保留到后台退出，因此不承诺开关关闭后工作集立即回到首次启动值。

HTTP 工作线程读取不可变状态快照；操作提交到后台线程。固定资源表禁止任意文件路径，嵌入资源首次访问才缓存。请求读取使用 4 KiB 缓冲区与总期限，并限制并发、大小、来源、Host/Origin 和操作频率。网页无需前端构建或外部 CDN。

设置界面使用 .NET Framework 4.8 自带的 WPF。UI/Theme.xaml（样式）与 UI/Panel.xaml（布局）作为资源嵌入主 EXE，由 XamlReader 在运行时加载，因此无需 MSBuild XAML 编译，build.ps1 仍可只用系统 csc.exe 构建。XAML 不含 x:Class 与事件，PanelWindow 按名称查找控件并绑定行为。Theme 按注册表 AppsUseLightTheme 选择深浅色画刷，并在设置变化时更新；Windows 11 22H2 及以上通过 DWMWA_SYSTEMBACKDROP_TYPE 使用 Mica，失败时回退为纯色背景。

PanelWindow.Preview.cs 只构造示例视图用于文档，不启动屏幕控制、驱动、快捷键、守护或网页。添加设置项应在 Panel.xaml 增加控件、扩展管道快照与明确操作，不把后台生命周期重新放回设置窗口。
