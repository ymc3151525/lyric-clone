# desktop

## 职责
负责桌面歌词窗：拖动、锁定后点击穿透、双行/描边/对齐。

不负责：取歌词、其它浮层。

## 公开接口
- `DesktopLyricsWindow`：由 [shell](../shell/README.md) 创建。
- 锁定：`WS_EX_TRANSPARENT` 点击穿透；解锁从托盘或设置。

## 依赖
- [playback](../playback/README.md)：`NowPlayingService`。
- [settings](../settings/README.md)：`AppSettings.Desktop`。
- [shared](../shared/README.md)：`LyricLineView`、主题。

## 关键约定
- 默认可不开启（设置里 `desktop.enabled`）。
- 位置写入设置；多显示器未作为必须验收项。

## 当前实现
`src/LyricClone.App/Views/DesktopLyricsWindow.*`  
`src/LyricClone.App/Interop/NativeWindow.cs`  
测试：`tests/LyricClone.Flows.Tests/ViewsFlowTests.cs`（F09 desktop lock）
