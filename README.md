# LyricClone

个人学习用的 Windows 歌词浮层。托盘里打开，跟着系统正在播放的歌曲显示歌词，支持桌面歌词和灵动岛。

协作与后续功能以 [AGENTS.md](AGENTS.md) 为最高规范；模块说明见其中的模块索引。

灵动岛形态向 [Lyricify](https://github.com/WXRIW/Lyricify-App)（WXRIW）的公开设计致谢，许可为 [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/)。本仓库为独立实现，不含 Lyricify 的源码、图标或文案。

## 功能

- 读取 Windows 系统媒体控制（SMTC）里当前播放的曲目
- 歌词来源（按顺序尝试）：本机播放器缓存（QQ / 网易云 / 酷狗本地文件）、嵌入歌词、[LRCLIB](https://lrclib.net)
- 桌面歌词、灵动岛（可贴屏幕边缘）、全屏歌词、任务栏歌词
- 英文歌词可配对显示中文翻译；中文歌词不自动翻译
- 设置保存在 `%AppData%\lyric-clone`

不使用非官方的 QQ / 网易云 / 酷狗 HTTP 歌词接口。

## 环境

- Windows 10 或更高
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## 构建与运行

```powershell
dotnet build LyricClone.sln
dotnet run --project src/LyricClone.App
```

生成的可执行文件：

`src/LyricClone.App/bin/Debug/net10.0-windows10.0.19041.0/LyricClone.exe`

运行后在通知区域打开托盘图标。

## 测试

```powershell
dotnet test LyricClone.sln
```

## 许可

源代码按本仓库声明使用。灵动岛交互形态的设计思路归功于 Lyricify（WXRIW），CC BY-SA 4.0。
