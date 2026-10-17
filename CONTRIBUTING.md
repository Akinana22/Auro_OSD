# 贡献指南 / 提交说明

感谢参与 **Auro_OSD**。本文说明本项目的开发、构建、验证与提交流程，请在提交前阅读。

---

## 项目简介

Auro_OSD 是一个 Windows 常驻托盘工具：通过 **DDC/CI** 用软件切换显示器输入源（PC 的 DP 与其它 HDMI 设备），
并可选地基于视频采集卡信号自动切换，同时按配置施加投影模式与默认音频设备。

技术栈：

- .NET 8 / WinForms（`net8.0-windows10.0.19041.0`，x64）
- DDC/CI：`dxva2.dll`（`GetVCPFeatureAndVCPFeatureReply` / `SetVCPFeature`）
- 采集：OpenCvSharp4 + DirectShow
- 音频默认设备：AudioSwitcher.AudioApi.CoreAudio
- 显示器信息：WMI（`WmiMonitorID`）

---

## 开发环境

- Windows 10 / 11（x64）
- .NET 8 SDK
- 若使用便携 SDK，先设置 `DOTNET_ROOT`：

```powershell
$env:DOTNET_ROOT = "C:\path\to\dotnet8"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
```

---

## 目录结构

```
src/Auro_OSD/
  Program.cs            入口与命令行
  Config/               配置模型、读写、开机自启
  Ddc/                  DDC/CI（显示器枚举、VCP 读写）
  Display/              投影拓扑、目标屏定位
  Audio/                默认播放/录音设备
  Capture/              采集卡抓帧、签名、快照与相似度
  Switching/            输入切换、自动切换服务、投影/音频施加
  Wizard/               识别向导、手动输入、自动切换列表、暂停弹窗
  Settings/             设置窗口
  Tray/                 托盘菜单、热键、切换编排
  Logging/              运行日志
  Osd/                  OSD 提示
  Hotkeys/              全局热键
  Assets/               图标与图片
```

---

## 构建

```powershell
# 开发构建
dotnet build Auro_OSD.sln -c Debug

# 发布：自包含单文件 -> .\dist\Auro_OSD.exe
.\build.ps1
```

`build.ps1` 支持参数：

```powershell
.\build.ps1 -Runtime win-x64 -Configuration Release
```

---

## 运行与命令行

直接运行托盘程序；诊断用命令行：

```
Auro_OSD.exe --list            列出物理显示器
Auro_OSD.exe --read            读取当前输入源（VCP 0x60）
Auro_OSD.exe --caps            读取 DDC 能力字符串
Auro_OSD.exe --set <n>         设置输入源
Auro_OSD.exe --capture [ms]    列出采集设备并检测信号
Auro_OSD.exe --capture-snapshot <png>   保存无信号快照
Auro_OSD.exe --capture-check <png>      当前帧与快照比对
Auro_OSD.exe --capture-probe <png> <秒> <间隔ms> <api>
Auro_OSD.exe --capture-reopen <png> <秒> <间隔ms> <reopenMs>
Auro_OSD.exe --audio-list      列出播放/录音设备
Auro_OSD.exe --audio-set <play|rec> <id>
```

---

## 分支与提交

- 主分支 `main` 始终保持可构建、可发布。
- 功能 / 修复使用独立分支，例如：

```
feat/采集卡自动切换
fix/目标屏匹配
docs/更新构建说明
```

- 提交信息使用**中文**，采用 Conventional Commits 前缀：

| 前缀 | 含义 |
| --- | --- |
| `feat:` | 新增功能 |
| `fix:` | 修复缺陷 |
| `docs:` | 文档变更 |
| `chore:` | 构建 / 依赖 / 杂项 |
| `refactor:` | 重构（不改变外部行为） |
| `test:` | 测试相关 |

示例：

```
fix: 切换后投影与音频未应用

- DDC 写入改为 2 秒内写 4 次
- 写入后按配置施加投影与默认音频设备
```

---

## 代码约定

- 沿用现有代码风格与模块边界，优先复用已有工具函数。
- 启用可空引用类型（`Nullable=enable`），新增代码尽量不引入空引用告警。
- 不添加无意义的注释；只在逻辑不直观处写简短说明。
- 新增第三方依赖前请先讨论，避免引入体积大或不可分发的库。
- UI 文案使用中文，与现有界面保持一致。

---

## 提交前验证（手动）

本项目目前没有自动化测试，请在提交前至少验证：

1. `Auro_OSD.exe --list` 能列出物理显示器，且目标显示器可被识别。
2. `Auro_OSD.exe --read` 能读取当前输入源。
3. 手动切换 DP / HDMI1 / HDMI2：
   - 输入源正确切换；
   - **投影模式**按配置应用；
   - **默认播放/麦克风**按配置切换（日志可见 `audio set ...`）。
4. 自动切换列表：创建 / 查看 / 清除快照，采集设备信号检测正常。
5. 重复启动程序会被单实例拦截（提示“已在运行”）。
6. 托盘右键「打开数据目录」能打开 `%APPDATA%\Auro_OSD`。
7. 开启日志后检查 `%APPDATA%\Auro_OSD\logs\autoswitch.log` 无异常。

---

## 不要提交

以下内容已在 `.gitignore` 忽略，请勿强行提交：

- 构建产物：`bin/`、`obj/`、`dist/`
- IDE：`.vs/`、`.idea/`、`*.user`、`*.suo`
- 运行数据：日志、快照、本地配置、`*.bak`、`*.log`
- 任何密钥、令牌、个人隐私信息（如真实显示器序列号）

---

## 发布

1. 更新 `src/Auro_OSD/Auro_OSD.csproj` 的 `<Version>`。
2. 执行 `.\build.ps1`，产物为 `dist\Auro_OSD.exe`。
3. 打标签并推送：`git tag vX.Y.Z && git push origin vX.Y.Z`。
4. 在 GitHub Releases 上传 `Auro_OSD.exe` 并附变更说明。

---

## Pull Request

- 一个 PR 只做一件事，标题清晰。
- 描述中包含：动机、主要改动、验证方式（对照上面的验证清单）。
- 涉及界面或配置结构变化时，请同步更新 `README.md`。
