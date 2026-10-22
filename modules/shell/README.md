# shell

## 职责
负责托盘图标、单实例、开机启动、全局快捷键，以及各歌词窗口的打开/关闭。

不负责：SMTC 读曲、歌词获取与解析、各浮层的绘制细节。

## 公开接口
- `App`：进程入口、托盘菜单、窗口生命周期。
- `Autostart`：HKCU Run 开机启动。
- `HotkeyService`：注册/注销全局热键（开关键盘歌词窗、锁定桌面歌词）。
- `ThemeService`：跟随系统/设置切换亮暗主题。

调用方：用户（托盘、热键）；各视图模块只被本模块创建，不反向控制托盘。

## 依赖
- [playback](../playback/README.md)：`NowPlayingService`、播放控制。
- [settings](../settings/README.md)：`ISettingsStore`。
- [island](../island/README.md)、[desktop](../desktop/README.md)、[taskbar](../taskbar/README.md)、[fullscreen](../fullscreen/README.md)、[stage](../stage/README.md)、[library](../library/README.md)、[settings](../settings/README.md)：创建对应窗口。
- [shared](../shared/README.md)：文案 `Loc`。

## 关键约定
- 单实例互斥名 `lyric-clone.single-instance`。
- `--autostart` 启动时跳过「没有播放器」提示。
- 后续新文件放本目录，加入 `src/LyricClone.App/LyricClone.App.csproj`。

## 当前实现
`src/LyricClone.App/App.xaml(.cs)`、`Services/Autostart.cs`、`Services/HotkeyService.cs`、`Services/ThemeService.cs`、`Services/PlayerHintService.cs`
