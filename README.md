# Auro_OSD

通过 **DDC/CI** 在 Windows 上用软件切换显示器的输入源（PC 的 DP 与其它 HDMI 设备），
无需按显示器按键、无需额外硬件。

目标显示器可配置，并内置“手动填写输入源 / 自动识别输入源”来学习该显示器各输入口对应的码值。

## 特性

- **纯 DDC/CI 切换**：直接调用 `dxva2.dll`，不依赖任何外部程序。
- **托盘菜单切换**：`DP（名称）` / `HDMI1（名称）` / `HDMI2（名称）`。
  **未绑定码值或名称为空的项显示为 `NULL` 且置灰不可点。**
- **自定义全局快捷键**：默认不设置，在设置中录入（如 `Ctrl+Alt+1`），仅对已绑定项可用。
- **输入源识别**：提供 **手动填写输入源** 与 **自动识别输入源** 两个入口。
  - 自动识别：引导用显示器 OSD 逐一切到 DP/HDMI1/HDMI2 并读取码值，支持重试/跳过/上一步。
  - 手动填写：直接查看当前 VCP `0x60` 并填写各输入源码值，`重置` 仅解绑码值。
- **未绑定语义**：输入源必须先绑定码值，才能设置名称与快捷键。
- **设置/手动窗口**：无“取消”按钮，`保存` 即时生效且不关闭窗口，仅标题栏 `X` 关闭。
- **OSD 提示**：
  - 设置页选择目标显示器时（含打开设置时）在其屏幕显示 `目标显示器` + 完整名称，持续 3 秒；
  - 进入识别向导时先在当前态显示同样提示 3 秒，随后自动切换为复制；
  - 其它切换过程不显示 OSD。
- **稳定目标屏绑定**：目标屏以 **`DeviceString` + `Serial` + `DeviceId`（稳定前缀）三者同时匹配**，
  不使用 `\\.\DISPLAYn` 这类会随拓扑重排的输出编号；可跨 复制/扩展 拓扑稳定，并能区分同型号多台。
- **单实例**：程序已在运行时再次启动会提示“Auro_OSD 已在运行”，不会多开。
- **打开数据目录**：托盘右键可直接打开 `%APPDATA%\Auro_OSD`。
- **自动切换（采集卡信号检测）**：检测视频采集设备（如采集卡）是否有视频信号，自动把目标屏切到对应 HDMI。
  在托盘/设置的 **自动切换列表** 中勾选、排序并绑定采集设备；目标屏在 DP 时才自动发起，信号丢失时切到下一个活跃项、无则切回 DP。
  无信号判定基于 **无信号参考帧 + 相似度**：点【创建快照】会连拍 10 帧并弹出选择窗口，
  在其中**勾选**采集卡显示“无信号”画面的帧（可多选，单击图片放大）；软件以所选帧的**最小两两相似度**为基线
  （低于 0.95 按 0.95 计），阈值为 `基线−2%`（0.9–0.99）。运行时每 **250ms** 抓帧，与任一所选参考的
  最大相似度 ≥ 阈值即视为无信号。所选帧以 `snap_<hash>_k.png` 保留在主参考旁，列表点 **查看** 可翻页浏览，**清除** 删除参考。
  参考存于 `%APPDATA%\Auro_OSD\snapshots\`。**未创建快照或参考无效的设备不可用。**
- **音频随输入切换**：在设置中可分别指定「切到 DP」与「切出 DP」时的默认**播放设备**与**麦克风设备**；切换输入源时自动切换（手动与自动均生效）。设备缺失或已是默认则跳过。
- **手动回 DP 定时暂停**：通过软件手动切回 DP 时弹出定时器询问（1/5/10/30 分钟或不设置），在设定时间内暂停自动切换。
- **投影模式管理**：切到 DP / HDMI1 / HDMI2 时分别应用各自配置的投影模式（默认：DP=扩展，HDMI=复制）。
- **切换流程**：每次切换先在 **2 秒内写 4 次**输入码，再按配置施加投影与音频（不回读校验、不回退）。
- **自动识别提醒**：提示在显示器 OSD 中关闭 `自动选择输入源 / Auto Scan`。

## 系统要求

- Windows 10 / 11（x64）
- 显示器需支持并开启 DDC/CI
- 采集卡检测使用 OpenCvSharp（DirectShow），随程序打包，无需额外安装

## 构建

需要 .NET 8 SDK。若使用便携 SDK：

```powershell
$env:DOTNET_ROOT = "C:\path\to\dotnet8"
.\build.ps1
```

产物：`dist\Auro_OSD.exe`（自包含单文件，免安装）。

开发构建：

```powershell
dotnet build Auro_OSD.sln -c Debug
```

## 命令行

```
Auro_OSD.exe --list       列出物理显示器
Auro_OSD.exe --read       读取当前输入源 (VCP 0x60)
Auro_OSD.exe --caps       读取 DDC 能力字符串
Auro_OSD.exe --set <n>    设置输入源
Auro_OSD.exe --capture [ms]  列出视频采集设备并检测信号（默认 6 秒）
Auro_OSD.exe --capture-snapshot <png>  保存无信号快照并报告两帧相似度
Auro_OSD.exe --capture-check <png>     将当前帧与已保存快照比对（打印相似度）
Auro_OSD.exe --capture-probe <png> <秒> <间隔ms> <api>   连续采样对比（诊断）
Auro_OSD.exe --capture-reopen <png> <秒> <间隔ms> <reopenMs>  周期重开采集（诊断）
Auro_OSD.exe --audio-list              列出默认播放/录音设备
Auro_OSD.exe --audio-set <play|rec> <id>  设置默认播放/录音设备
```

## 使用

1. 运行 `Auro_OSD.exe`，右下角托盘出现图标。
2. 右键 → **自动识别输入源**：按提示用显示器 OSD 依次切到 DP / HDMI1 / HDMI2。
   点“识别”（再次点击为“重试”），识别成功后点“下一步 / 完成”；不需要的输入可直接“跳过”。
   > 识别前请确保该输入源已接入并有视频信号；无信号时可能读到错误值。
3. 右键 → **手动填写输入源**：查看当前 VCP `0x60`，直接填写各输入源码值；`重置` 解绑码值，`保存` 即时生效。
4. 右键 → **自动切换列表**：勾选并排序 HDMI 输入源、为每行选择对应的视频采集设备，点 **保存**。
5. 右键 → **设置**：选择目标显示器、为已绑定的输入填写名称与快捷键、选择投影模式与音频设备；
   点 **保存** 即时生效（窗口不关闭，点标题栏 `X` 关闭）。
6. 之后即可通过托盘菜单或快捷键切换；若通过软件手动切回 DP，会弹出定时器询问以临时暂停自动切换。
7. 需要查看数据时，右键 → **打开数据目录**。

## 数据目录

`%APPDATA%\Auro_OSD\`

- `config.json`：配置
- `logs\autoswitch.log`：运行日志（托盘菜单“启用日志”后写入，按大小滚动）
- `snapshots\`：自动切换的无信号参考帧

程序启动时会自动创建该目录。

## 配置文件

示例（`config.json`）：

```json
{
  "TargetMonitor": {
    "DeviceString": "MSI MAG 276QRY28",
    "Serial": "SN0000000000",
    "DeviceId": "MONITOR\\XXXXXXX"
  },
  "InputMap": { "Dp1": 15, "Hdmi1": 17, "Hdmi2": null, "LearnedByWizard": true },
  "Labels": { "Dp": "PC", "Hdmi1": "NS", "Hdmi2": null },
  "Hotkeys": { "Dp": null, "Hdmi1": null, "Hdmi2": null },
  "Projection": { "OnDp": "Extend", "OnHdmi1": "Clone", "OnHdmi2": "Clone" },
  "Osd": { "DurationMs": 3000 },
  "AutoSwitch": {
    "PollMs": 1000,
    "ActiveHoldMs": 1500,
    "Order": [
      {
        "Input": "Hdmi1",
        "Enabled": true,
        "CaptureDevice": "\\\\?\\USB#...#GLOBAL",
        "NoSignalSnapshot": "%APPDATA%\\Auro_OSD\\snapshots\\snap_XXXXXXXXXXXX.png",
        "SimilarityThreshold": 0.98
      }
    ]
  },
  "Audio": {
    "OnDpPlaybackId": null,
    "OnDpRecordingId": null,
    "OffDpPlaybackId": null,
    "OffDpRecordingId": null
  },
  "Log": { "Enabled": false },
  "AutoStart": false
}
```

> `TargetMonitor` 三项均为硬件身份字段：`DeviceId` 只保存稳定前缀（如 `MONITOR\CND5CAC`），不含实例号。

## 注意事项

- **建议在显示器 OSD 中关闭【自动选择输入源 / Auto Scan】**，否则可能与软件切换冲突。
- 部分显示器在切换输入后 DDC 会短暂无响应；本工具按“重新枚举显示器句柄”的方式操作以规避。
- 识别向导在某输入下读不到数据会直接终止并提示 **无法兼容该显示器**（可在超时前“跳过”该输入）。
- 本项目不做旧配置迁移：字段调整后，旧配置中的对应项会被忽略并回落默认。
