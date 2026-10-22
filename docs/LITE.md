# Filee Lite — 打包、安装与使用指南

Filee Lite 是为创意比赛精简的 Windows 版本：拖住文件划一个圈，甜甜圈菜单就地弹出，松手完成转换。
只保留图片互转（PNG / JPG / WebP / BMP / ICO / GIF）与 PDF 互转（图片→PDF、合并、拆分、PDF→图片），
整个程序打包为**一个约 26 MB 的 exe**，无需安装。

---

## 一、打包（在 Windows 机器上）

### 前置要求

| 项目 | 要求 |
| --- | --- |
| 操作系统 | Windows 10 / 11（x64） |
| .NET SDK | 10.0（仓库根目录 `global.json` 已锁定版本，从 [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0) 安装） |
| PowerShell | 系统自带 Windows PowerShell 或 PowerShell 7 均可 |

### 步骤

```powershell
# 1. 切到精简分支
git checkout competition-lite

# 2.（可选）跑一遍测试确认一切正常
dotnet test Filee.slnx -c Release

# 3. 打包单 exe（可选 -Zip 同时生成 zip 包和校验文件）
pwsh build/build-lite.ps1 -Zip
```

### 产物

输出在 `build/Releases-lite/` 下：

```
Filee-Lite-1.12.0-win-x64/
└── Filee.exe          约 26 MB，内含运行时与 Skia / PDFium 原生库，单文件即全部
Filee-Lite-1.12.0-win-x64.zip       （-Zip 时）含 exe 与 LICENSE
Filee-Lite-1.12.0-win-x64-SHA256SUMS.txt   （-Zip 时）zip 的校验值
```

### 常用参数

| 参数 | 作用 |
| --- | --- |
| `-Runtime win-x64` | 目标架构，默认 x64，可选 `win-x86` / `win-arm64` |
| `-Zip` | 额外生成 zip 包（内含 LICENSE）与 SHA256 校验文件 |
| `-NoTrim` | 关闭 IL 裁剪。**若发布版启动或拖拽异常，先用它重新出包**（体积约增加 25 MB） |
| `-Version 1.12.0` | 覆盖版本号（默认取 `Directory.Build.props`） |

> 说明：发布时 `FileeRelease=false`，所以这个 exe **不会**注册资源管理器右键菜单、开机自启等任何系统项，拔掉即走，评委机器不留痕迹。

---

## 二、安装

无需安装：把 `Filee.exe` 复制到任意文件夹（桌面、U 盘、下载目录均可），双击即可运行。

**首次运行注意两点：**

1. **SmartScreen 提示**（未签名的exe会触发）：弹窗点「更多信息」→「仍要运行」。
2. **首次启动稍慢**：单文件内的原生库（Skia、PDFium）会解压到临时目录，之后再启动恢复正常速度。

---

## 三、启动与使用

### 启动

双击 `Filee.exe`。程序驻留**系统托盘**（通知区域），不占任务栏；重复双击不会出现第二个实例。

托盘图标右键菜单：

- **打开** — 打开主窗口（设置、历史记录）
- **暂停 / 恢复** — 临时停用手势
- **退出** — 真正退出程序

### 核心玩法：划圈转换

1. 在资源管理器（或桌面）选中一个或多个文件；
2. **按住 Ctrl + 鼠标左键拖动**，在文件上划一个圈（默认手势，可在设置中改为其他按键）；
3. 松手处弹出**甜甜圈菜单**——按文件类型自动选好工具环：图片文件是图片环，PDF 是 PDF 环，混合文件是通用环；
4. 点击一个扇区（如 PNG / JPG / PDF / 合并 / 拆分 / 旋转 / 翻转 / 灰度）即开始转换；
5. 屏幕角落的**进度提示（Toast）**显示转换结果，文件默认输出在源文件旁（如 `photo.jpg → photo.png`，重名自动加 `(2)`）。

其他手势：

| 操作 | 效果 |
| --- | --- |
| `Ctrl + Alt + Space` | 对当前选中的文件直接弹出菜单（不划圈） |
| 菜单上**滚轮 / Tab** | 翻到甜甜圈背面（旋转、翻转、灰度等工具预设） |
| `Esc` | 关闭菜单不转换 |

### 数据与卸载

- 用户数据（设置、预设、历史）保存在 `%APPDATA%\Filee`（`C:\Users\<用户名>\AppData\Roaming\Filee`）。
- **卸载 = 删除 `Filee.exe`**（可选：再删掉上面的数据文件夹）。系统里没有注册表项、服务、自启动项需要清理。

---

## 四、常见问题

| 现象 | 处理 |
| --- | --- |
| 双击没反应 | 看托盘有没有图标——程序就住在那里，不开窗口 |
| 拖拽划圈无菜单 | 确认在资源管理器/桌面操作（浏览器里不生效）；托盘未处于「暂停」；检查设置中的触发手势 |
| 划圈功能异常（发布版专属） | IL 裁剪误删了文件管理器交互所需的代码：用 `build/build-lite.ps1 -NoTrim -Zip` 重新出包 |
| 体积变大到 ~50 MB | 就是上一条的 `-NoTrim` 结果，属正常回退 |
| 想清理转换历史 | 主窗口 → 设置/历史页，或直接删除 `%APPDATA%\Filee` 下的文件 |
