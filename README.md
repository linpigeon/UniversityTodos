# 大学事项综合待办 · UniversityTodos

用 Avalonia 写的 Windows / Android 待办应用：自动聚合**长江雨课堂**、**学习通**、**批改网**
的作业、考试与公告，再加上课表与自建事项，以仿 Microsoft To Do 的 Fluent 界面呈现。

> 仓库：GitHub `linpigeon/UniversityTodos` · Gitea `lin/UniversityTodos`。
> 本地检出目录名只影响命令行里的路径，不代表仓库身份——两者可以不同，脚本与文档一律用相对路径。
> 代码标识（程序集名、命名空间、Android 包名）仍沿用 `HomeworkReminder`，
> 以便老用户的本地数据与自动更新不中断；安装版的数据目录改为安装目录下的 `Data`（见下文）。

![界面](docs/screenshots/app-main.png)

## 功能与特性

* **作业清单**：按课程归集雨课堂 / 学习通 / 批改网的作业与考试，显示截止时间与剩余天数，逾期自动标红

* **截止提醒**：临近截止按提前量提醒、每天可发一条逾期汇总；休眠恢复后 30 分钟内会补发

* **自建待办**：标题、截止时间、提醒时间、优先级、标签、备注，参与搜索/星标/我的一天与提醒

* **搜索与星标**：按标题/课程/备注/标签搜索；星标与「加入我的一天」都是本地标记，不改平台数据

* **真实完成状态**：完成进度直接来自雨课堂的作答记录，而不是「截止时间过了没有」

* **公告未读**：公告按已读/未读分组，支持单条已读与全部已读

* **两种登录方式**：应用内嵌官方登录页微信扫码；没有 WebView2 的机器可改用粘贴 Cookie 登录

* **Fluent 半透明界面**：亚克力磨砂背景，支持自定义壁纸与透明度滑块

* **深色模式**：浅色 / 深色 / 跟随系统，即时切换

* **托盘常驻**：关闭时可选择「最小化运行」，收到新作业随时点开

* **隐私安全**：登录态文件用 Windows DPAPI 加密存放，只有本机当前用户能解开

* **学期可配置**：编辑数据目录下的 `settings.json` 即可切换学期与学校，不用改代码

* **定时自动同步**：默认每 30 分钟同步一次作业列表，间隔可在 `settings.json` 调整（`0` = 关闭）

* **新作业通知**：发现新作业时发 Windows 系统通知，点击直达该课程的雨课堂页面

* **课表**：可从正方教务一键导入，也可手工增删改或从 CSV/TSV 模板导入；「我的一天」顶部显示今天的课

* **数据备份**：设置 → 关于可导出/恢复数据 zip；数据版本高于程序时自动进入只读，避免降级覆盖

* **桌面小组件**：仿滴答清单的桌面挂件，半透明玻璃面板、可拖动、可缩放，位置与尺寸自动记忆，浅色/深色跟随主程序

## 安装（推荐）

从 release 下载 `UniversityTodos-<tag>-Setup.exe` 双击安装：

- 默认安装到 `%LOCALAPPDATA%\Programs\大学事项综合待办`（**按用户安装，不需要管理员权限**），
  向导里可以改到其它可写目录。
- 自动创建**桌面快捷方式**与开始菜单项（含卸载入口），并写入「设置 → 应用」的卸载信息。
- 安装版把**程序、数据、缓存**都收在安装目录下：
  - `<安装目录>\Data`：设置、登录态、待办缓存、自建待办、提醒状态、课表；
  - `<安装目录>\Cache\WebView2`：登录页浏览器缓存；
  - `portable.flag`：安装模式标记。删掉它，应用会回到兼容模式
    （数据在 `%LOCALAPPDATA%\HomeworkReminder`）。
- 安装向导检测到旧版数据时可勾选「迁移旧数据」；卸载默认**保留** `Data`。
- 已经在用旧版？打开 **设置 → 关于 → 下载安装版…**，应用会自动下载安装包；下载后先退出再运行。
- 安装包未做代码签名，Windows SmartScreen 可能提示，选择「更多信息 → 仍要运行」即可。

> 从旧版**自动更新**（zip）过来的用户不会切到安装模式：exe 旁没有 `portable.flag`，
> 仍使用原来的 `%LOCALAPPDATA%\HomeworkReminder`，不需要迁移。

## 快速开始

双击运行 `HomeworkReminder.Desktop.exe`，**启动直接进主界面**（登录不再是启动门禁）。
没有登录态时列表顶部会有一条「去登录」引导，点它直达设置页的「账号」选项卡。

在**设置 → 账号**里可以分别登录三个数据源：

1. **雨课堂**——点「扫码登录 / 重新登录」，应用内嵌官方登录页，微信扫码即可；
   缺少 WebView2 运行时时点「使用 Cookie 登录」，从浏览器 F12 → Network
   复制任意 `v2/api/web` 请求的 `cookie` 请求头粘贴进去；
2. **学习通（超星）**——点「登录学习通」，在内嵌登录页用账号密码 / 短信 / 邮箱登录，
   登录成功自动捕获会话；
3. **批改网（句酷）**——点「登录批改网」，在内嵌的批改网页面输入账号密码，
   登录成功自动捕获会话，同步**未完成作文**为待办。

三者可以只登一个，也可以任意组合；待办列表会把各来源的作业合并显示。
登录态失效时不会弹回登录页，而是列表顶部横幅提示，已缓存的列表继续可见。

> 三个数据源都**只保存会话 Cookie**（Windows 下用 DPAPI 加密），
> 不保存账号密码：登录一律在应用内嵌的官方页面里完成。

## 自动同步与通知

默认每 **30 分钟**自动同步一次。**首次同步只建立基线**，不会把已有作业全报一遍；
之后每次同步只对**新增**作业发通知。判定用稳定 Id 而非标题，改标题不会误报。

界面上「剩余 N 天 / 已过期」这类文案由独立的分钟级计时器刷新（不依赖同步成功），
因此即便关掉自动同步或长时间离线，倒计时和逾期标记也会照常走，「我的一天」
跨过午夜会自动重算。

相关设置（`settings.json`，与 `session.json` 同目录，改完重启生效）：

| 字段                      | 默认    | 说明                                              |
| ----------------------- | ----- | ----------------------------------------------- |
| `AutoRefreshMinutes`    | `30`  | 自动同步间隔（分钟）。`0` = 关闭；低于 5 会被夹到 5，避免频繁请求被风控      |
| `NotifyOnNewHomework`   | `true` | 是否发系统通知                                        |
| `WidgetVisible`         | `false` | 是否显示桌面小组件                                      |
| `WidgetX` / `WidgetY`   | —     | 小组件位置，拖动后自动写入                                  |
| `WidgetGroup`           | `myday` | 小组件当前分组（myday / homework / planned / all）      |

### 通知链路自检

通知涉及注册表、开始菜单快捷方式与 WinRT 三层，出问题时不易定位，因此单独留了自检：

```powershell
.\HomeworkReminder.Desktop.exe --toastcheck
```

逐项报告：AUMID 注册 → 开始菜单快捷方式 → toast XML 生成与转义 → WinRT 投递（失败给 HRESULT）。

> **实现说明**：未打包（unpackaged）的 Win32 应用发 toast 需要三个条件，缺一不可：
> ① 注册 `HKCU\Software\Classes\AppUserModelId\<aumid>`；
> ② 在开始菜单放一个带 `System.AppUserModel.ID` 属性的快捷方式
> ——实测**只做 ① 不够**，`CreateToastNotifier` 会返回 `0x80070490`（找不到元素）；
> ③ 走 WinRT `ToastNotificationManager`。
> 三处写入都在 HKCU 与用户自己的开始菜单目录，**不需要管理员权限**。
> 本仓库的离线包源没有任何通知相关 NuGet 包，这一层是手写的 COM 互操作
> （`Services/WinRtToast.cs`），与 `Dpapi` 同一取舍。

## 桌面小组件

从**托盘菜单 → 显示桌面小组件**开启，再点一次关闭。

* 无边框、透明、普通窗口层级（不置顶；从托盘/主界面召唤时会到最前），左键拖动移动位置、拖边缘/角落调整大小（停下即记忆）
* 直接复用主界面已同步的数据，**不额外请求网络**，与主界面始终一致
* 双击条目在浏览器打开；点勾选框在应用内标记
* 只显示「服务端未完成 **且** 应用内未勾掉」的事项（雨课堂、学习通、批改网的作业都在内，
  公告按未读计），与主界面口径一致

![桌面小组件](docs/screenshots/widget.png)

## 数据存放

应用有两种数据模式，**看 exe 同目录有没有 `portable.flag`**：

| 模式 | 数据 / 缓存位置 | 适用 |
|---|---|---|
| **安装/便携模式**（有 `portable.flag`） | `<安装目录>\Data\` 存全部 JSON；`<安装目录>\Cache\WebView2\` 存 WebView2 缓存 | 安装包安装；或手动在 exe 旁建 `portable.flag` |
| **兼容模式**（没有标记） | `%LOCALAPPDATA%\HomeworkReminder\` 存全部 JSON；WebView2 缓存在 exe 旁边 | 旧版自动更新、直接解压 zip |

`Data` / 用户目录里的文件：

| 文件 | 内容 |
|---|---|
| `session.json` | 雨课堂登录态（DPAPI 加密） |
| `chaoxing-session.json` | 学习通登录态 |
| `pigai-session.json` | 批改网登录态（同样 DPAPI 加密） |
| `local-state.json` | 本地勾选 / 已读 / 星标 / 我的一天 |
| `manual-todos.json` | 自建待办 |
| `reminder-state.json` | 提醒去重状态 |
| `settings.json` | 壁纸 / 透明度 / 主题 / 提醒等设置 |
| `todos.json` | 待办缓存（各来源的作业都在里面） |
| `schedule.json` | 课表 |
| `startup.log` | 启动日志 |

数据目录可用环境变量 `HOMEWORKREMINDER_DATA_DIR` 覆盖（优先级最高）。

### 从旧版迁移数据（安装版首次使用）

> 先**退出**旧程序，再复制文件；迁移前建议把 `%LOCALAPPDATA%\HomeworkReminder`
> 整个目录压缩备份一份。旧目录确认无误前不要删。

**方式一：安装向导自动迁移（推荐）**

1. 运行 `UniversityTodos-<tag>-Setup.exe`；
2. 安装向导检测到 `%LOCALAPPDATA%\HomeworkReminder` 时，会显示「迁移旧数据」页，保持勾选；
3. 安装完成后打开应用，到 **设置 → 关于 → 数据**，确认「数据位置」显示
   `<安装目录>\Data`；
4. 检查作业/待办/课表/登录态是否齐全；缺什么再从备份补。

**方式二：手动复制（跳过了迁移页，或想自己控制）**

1. 如果用的是安装版：`<安装目录>\Data` 已经存在，直接进行第 3 步；
2. 如果用的是 zip：把 exe 放进一个**可写目录**，在 exe 旁新建空文件 `portable.flag`
   和 `Data` 文件夹（不建标记就仍是兼容模式，数据继续留在旧目录，不需要迁移）；
3. 把 `%LOCALAPPDATA%\HomeworkReminder\*.json` 复制到 `Data\`
   （只复制 JSON，不要复制 `startup.log`、`.WebView2` 缓存）；
4. 启动应用，按上面的方法核对「数据位置」。

**换机 / 换 Windows 用户时**：`session.json`、`chaoxing-session.json`、`pigai-session.json`
是 DPAPI 密文，只能在同一台机器的同一 Windows 用户下解密；迁移后如果提示未登录，
重新扫码/登录一次即可，其余待办与课表不受影响。

**想回到旧位置**：删除 `<安装目录>\portable.flag` 后重启，应用会重新读写
`%LOCALAPPDATA%\HomeworkReminder`（安装版 `Data` 里的数据会留在原处，可手动合并）。

## 代码签名（需要自备证书）

当前 release 的 exe 与安装包**没有代码签名**，Windows SmartScreen 会提示「未知发布者」。
这不是脚本能凭空补上的：需要一张受信任的代码签名证书（OV 或 EV），仓库里没有、也不应该提交证书与密码。

拿到证书后（需要 Windows SDK 的 `signtool.exe`）：

```powershell
# 1. 先签 exe，再打安装包，安装包内才是已签名的程序
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
  /f D:\cert.pfx /p <密码> publish\trim\HomeworkReminder.Desktop.exe

.\tools\build-installer.ps1 -Version 1.4.5

# 2. 再签安装包
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
  /f D:\cert.pfx /p <密码> publish\UniversityTodos-Setup-1.4.5.exe

# 3. 校验签名
signtool verify /pa /v publish\UniversityTodos-Setup-1.4.5.exe
```

> 注意：签名只能证明「发布者身份」与「文件未被篡改」；**新证书仍需要积累 SmartScreen 声誉**。
> EV 证书通常能更快建立信任，OV 证书在下载量积累前仍可能被提示。
> 签完 exe 后要重新打 zip / 安装包，否则发布产物里的 exe 还是未签名的那一份。
> `release.ps1` 默认会重新 publish；签名版要么复用已签好的 `publish\UniversityTodos-<tag>-win-x64\` 并用 `-SkipBuild`，要么把签名步骤接进脚本。当前 v1.4.5 发布包仍是未签名版本。
## 打包发布

```powershell
.\tools\publish.ps1 -Mode trim   -NoPdb   # 裁剪单文件：40.8 MB，双击即用（推荐分发）
.\tools\publish.ps1 -Mode single -NoPdb   # 完整单文件：约 100 MB，兼容性最好
.\tools\publish.ps1 -Mode fdd             # 框架依赖：体积最小，目标机需装 .NET 10 运行时
.\tools\publish.ps1 -Mode sc              # 自包含多文件
```

产物在 `publish\<模式>\` 下。**分发前请删除产物目录里的 `.WebView2` 文件夹**
（里面是本机登录页的 cookie 缓存），只发 exe 文件本身。

### 发布产物自检

```powershell
.\HomeworkReminder.Desktop.exe --selfcheck out.png   # 窗口/绑定自检 + 离屏渲染
.\HomeworkReminder.Desktop.exe --jsoncheck           # 序列化路径自检（裁剪版必过项）
.\HomeworkReminder.Desktop.exe --installercheck      # 解析最新安装包（传目录则实际下载校验）
.\HomeworkReminder.Desktop.exe --toastcheck          # 通知链路自检
.\HomeworkReminder.Desktop.exe --selfcheck-widget w.png  # 小组件渲染自检
.\HomeworkReminder.Desktop.exe --cxcheck             # 学习通课程任务点在线自检（只读）
.\HomeworkReminder.Desktop.exe --detailcheck         # 详情面板互斥回归（课程/待办不叠加）
.\HomeworkReminder.Desktop.exe --updatecheck         # 检查更新并走完整更新流程（视为用户已同意）
.\HomeworkReminder.Desktop.exe --updatecheck --target <exe>  # 同上，但替换指定 exe（沙箱演练用）
.\HomeworkReminder.Desktop.exe --pigailogin <账号> <密码>  # 批改网登录捕获链路
.\HomeworkReminder.Desktop.exe --pigaitest "<cookie>"      # 批改网下游链路
```

> **演练应用内更新**：把整套程序文件复制到临时目录，再用副本跑
> `--updatecheck --target <副本exe>`。`--target` 会替换那个副本而不是当前安装目录，
> 因此可以完整验证「下载 → 校验 → 替换 → 重启」而不影响正在用的安装。
> 要让更新真的被触发，被测版本的版本号必须低于远端 tag。

退出码为 0 即表示产物健康。`--cxcheck` / `--pigailogin` / `--pigaitest` 会真实访问
对应站点（只读，不修改远端状态），需要本机已有对应登录态或传入凭据。

> 渲染其他视图：`HWREMINDER_SELFCHECK_NAV=course` 可让 `--selfcheck`
> 渲染「课程」视图而不是默认的「作业」视图。

### 发版流程（含应用内更新所需的清单）

版本号的**唯一事实来源**是仓库根目录的 `Directory.Build.props`
（桌面与 Android 共同继承，桌面 exe 的版本信息也由它决定）：

```powershell
# 1. 改版本号（只改一处）
#    Directory.Build.props 的 <Version>；桌面 exe 与 Android versionCode/Name 都由它推导。

# 2. 打包（安装包依赖 publish\trim）
.\tools\publish.ps1 -Mode trim -NoPdb
.\tools\build-installer.ps1 -Version 1.4.5

# 3. 一键发布：校验版本一致性 → 打包 → 生成 update-manifest.json → 上传
$env:GITHUB_TOKEN='<GitHub 令牌>'; $env:GITEA_TOKEN='<Gitea 令牌>'
.\tools\release.ps1 -Tag v1.4.5 -Upload `
  -Installer .\publish\UniversityTodos-Setup-1.4.5.exe `
  -AndroidApk .\HomeworkReminder.Android\bin\Release\net10.0-android\com.CompanyName.HomeworkReminder-Signed.apk

# 只校验并打包、不上传
.\tools\release.ps1 -Tag v1.4.5

# 产物已生成、只想重传（例如补权限后）
.\tools\release.ps1 -Tag v1.4.5 -SkipBuild -Upload `
  -Installer .\publish\UniversityTodos-Setup-1.4.5.exe `
  -AndroidApk .\HomeworkReminder.Android\bin\Release\net10.0-android\com.CompanyName.HomeworkReminder-Signed.apk
```

**版本号必须与 tag 一致**，否则客户端会陷入更新循环（装了新版仍报旧版号，每次检查都提示有更新）。
`release.ps1` 会强制校验并在不一致时拒绝出包。

上传需要两个环境变量（**不要写进仓库**）：

| 变量 | 需要的权限 |
|---|---|
| `GITHUB_TOKEN` | 细粒度令牌需 **Contents: Read and write**（仅有读权限时创建 release 会返回 403 `Resource not accessible by personal access token`） |
| `GITEA_TOKEN` | 仓库写权限（访问令牌） |

> 两个源的失败**互不阻断**：任一失败会记录并继续，最后汇总以非零码退出，
> 并打印补传命令。所以可以先只配一个令牌、之后再补另一个。

要在某个 release 上启用「应用内一键更新」，该 release 至少包含下面前两个资产（安装包与 APK 可选）：

| 资产 | 作用 |
|---|---|
| `UniversityTodos-<tag>-Setup.exe` | Windows 安装包，首次安装用；不参与应用内自动更新 |
| `UniversityTodos-<tag>-win-x64.zip` | 更新包，**内含单个 exe**（trim 模式，约 18 MB） |
| `update-manifest.json` | 更新清单：确切文件名 + SHA256 + 大小（含 android / installer 字段） |

Android APK（`UniversityTodos-<tag>-android.apk`）也放在同一个 release，
但**不参与更新链路**——客户端自动更新只针对桌面端。

> 打包需要联网还原：自包含发布依赖 runtime pack，而仓库根的 `NuGet.Config`
> 把包源清成只剩本地文件夹源，因此 `publish.ps1` / `release.ps1` 会传 `-OnlineRestore`
> 把 nuget.org 作为**额外源**（不改动 `NuGet.Config` 本身）。

> 没有清单时仍可更新（按资产名兜底挑包），但**无法校验哈希**，会退化为只做版本兜底检查；
> 建议每个 release 都带上清单。

### 登录页排查开关（环境变量）

登录页是雨课堂自己的网页，出问题时只能看运行时的真实 DOM。以下开关只读页面、
不改行为，产物落盘在数据目录：

| 环境变量                        | 产物                | 作用                                                        |
| --------------------------- | ----------------- | --------------------------------------------------------- |
| `HWREMINDER_LOGIN_DOM_DUMP=1` | `login-dom.txt`   | 主文档的关键元素与坐标（原有）                                        |
| `HWREMINDER_LOGIN_PROBE=1`    | `login-probe.txt` | **主文档 + 各 iframe 的可见文本、按钮/链接、二维码元素**（登录方式实际在 iframe 里，只 dump 主文档看不到） |
| `HWREMINDER_PIGAI_DEBUG=1`    | `pigai-login.log` | 批改网登录探测：每次导航、Cookie 名字与域、捕获判定。只记 Cookie **名字**，不记值          |
| `HWREMINDER_CLEAR_WEBVIEW_LOGIN=1` | —            | 进入登录页前先清掉 WebView 里的持久化登录态，便于看到二维码而不是直接接管旧会话              |
| `HWREMINDER_FORCE_LOGIN=1`    | —                 | 正常运行模式下也强制停在登录覆盖层，便于在真窗口里复现问题                             |

### 批改网登录链路的自检
批改网登录**严格沿用雨课堂那条已跑通的路径**：打开覆盖层后起一个 async 轮询循环
（`Task.Delay` + `ConfigureAwait(true)` 回到 UI 线程），循环里先用页面内脚本探测
「登录是否真的生效」，确认后再读 Cookie 建会话。

- 探测脚本必须**同步返回**（WebView2 不等待 Promise，会把等待中的 Promise 序列化成 `{}`）；
- 判定看**响应正文**而不是状态码：批改网未登录时受限页面也返回 200，
  只是渲染「请先登录」；
- 每次探测外包 3 秒超时，避免页面跳转中途把循环拖住。

为什么不只等导航事件：用户输完账号密码要十几秒到几十秒，这期间不会再有导航事件；
登录提交是表单 POST，是否触发一次导航完成事件并不保证。

两个自检模式可分别验证上下游：

```powershell
# 上游：驱动登录页填表提交，验证「登录后能自动捕获并返回」
.\HomeworkReminder.Desktop.exe --pigailogin <账号> <密码> 150

# 下游：给一条真实会话 cookie，验证「接管 → 关覆盖层 → 同步 → 待办出现作文」
.\HomeworkReminder.Desktop.exe --pigaitest "<整条cookie>" 120
```

两者退出码 0 即通过。凭据/cookie 只从命令行读进内存，不落盘、不写日志。
（注意：`--pigailogin` 用脚本 `form.submit()` 提交，会绕过批改网自己的
密码加密 JS，因此**该模式可能被站点拒绝**——它验证的是探测与捕获机制，
不是站点能否登录；真实用户在页面里手动输入走的是站点自己的 JS。）

> 排查「微信快捷登录」时的已知事实：该入口渲染在 `/authorize/wx-qrlogin` 的 iframe 内，
> 且页面**不做自动探测**（`login.js` 里的探测标志 `Pt` 恒为 `false`，只有点击按钮才置位），
> 因此页面始终显示「微信快捷登录 / 扫码登录」二选一，而不是自动判断本机有没有微信。

## 项目结构

```
HomeworkReminder/                 共享项目：模型、服务、视图、ViewModel
  Models/                         TodoItem 等数据模型
  Services/                       每个数据源一套「客户端 + 登录 + 会话 + 同步」：
    YktApiClient / YktLoginService / SessionStore / SyncService
                                  雨课堂：课程日志 + 完成进度（唯一按课程扇出的来源）
    ChaoxingApiClient / ChaoxingLoginService / ChaoxingSessionStore / ChaoxingSyncService
                                  学习通：作业与考试两个列表 + 课程未完成任务点（均 HTML 解析）
    PigaiApiClient / PigaiLoginService / PigaiSessionStore / PigaiSyncService
                                  批改网：未完成作文列表（HTML 解析）
    AppJsonContext.cs             源生成 JSON 序列化上下文
    AutoSyncService.cs            定时同步 + 新作业判定（可脱离 UI 测试）
    Notifier.cs / WinRtToast.cs   系统通知抽象与 WinRT COM 互操作
    ---- 课表（教务系统导入）----
    Timetable.cs                  课表纯逻辑：节次时间表、单双周、周次换算、按周展开
    ZhengfangParser.cs            正方教务课表解析内核（kbList JSON / 列表视图 / 网格兜底）
    ZhengfangHtml.cs              课表页的轻量 HTML 抽取（去标签、实体、表格、课块）
    ScheduleStore.cs              schedule.json 读写与逐条校验
    ScheduleMerge.cs              合并导入（匹配键去重、事务式、不删既有）
    JwCaptureService.cs           教务页悬浮球注入与一键抓取
  Views/                          MainWindow / TodoShellView / LoginView
                                  ChaoxingLoginView / PigaiLoginView
                                  TimetableView（周视图网格）/ JwImportView（教务导入）
                                  WidgetView + WidgetWindow（桌面小组件）
  Styles/TodoTheme.axaml          仿 To Do 的 Fluent 控件样式（含深色主题）
  Styles/WidgetTheme.axaml        桌面小组件的深色半透明样式
HomeworkReminder.Desktop/         桌面宿主（启动、托盘、自检入口）
HomeworkReminder.Android/         Android 宿主（抽屉式外壳、原生 CookieManager 钩子）
tools/                            构建、发布、冒烟测试脚本
docs/                             接口实测文档与调研记录
  ykt-api.md                      雨课堂接口实测
  三源作业获取实现规格.md           ★ 三个源的获取实现规格（端点/参数/解析/坑），可直接照此移植
  界面设计规格.md                   ★ 界面设计规格（色板/令牌/控件/布局/响应式），可照此复刻
  数据源获取逻辑.md                本项目的整体架构与同步流程
  学习通状态探测.md                学习通任务点/作业/考试状态的探测定论
  教务课表导入.md                  ★ 正方课表解析口径、与 Electron 参照实现的差异、安全边界
```

## 开发

```powershell
.\tools\run.ps1                              # 构建并启动（数据目录指向 .appdata）
dotnet run --project tools\SyncSmoke         # 无界面跑一遍真实同步，打印待办结果
```

> 本仓库默认离线还原配置（`NuGet.Config` 指向仓库内 `packages/`，包缓存固定在
> `.nuget-packages/`）。在可联网的机器上可删除 `NuGet.Config`、`packages/`、
> `.nuget-packages/`，并去掉 `Directory.Build.props` 里的 `RestorePackagesPath`，
> 再直接 `dotnet restore`。
>
> 固定缓存目录是为了压过本机的用户级环境变量 `NUGET_PACKAGES`（它优先级高于
> `NuGet.Config`）：如果该变量指向一个不完整的缓存，IDE 里直接构建会报
> `NU1101: 找不到包 Avalonia...`，而命令行 `tools/build.ps1` 又能正常构建。
> `RestorePackagesPath` 是 MSBuild 属性，优先级最高，IDE / CLI / 脚本统一走仓库缓存。

## 已知限制

* 雨课堂没有「学生标记作业完成」的接口，应用内的勾选只影响本地视图

* 全部接口来自雨课堂网页端自身，非公开契约，前端改版可能导致失效；
  代码对每个调用做了容错（单课程失败不中断整次同步，并汇总为界面警告条）

* Android 端已验证可用（登录/同步/待办列表；登录页自动切换为账号密码/短信/邮箱登录，
  因为手机扫不了自己屏幕上的二维码）；iOS / Browser 端沿用模板但未经验证

