# playback

## 职责
负责读取 Windows SMTC 当前会话、稳定曲目元数据、外推播放进度，并向界面提供「正在播放」快照。

不负责：歌词文件从哪来、如何解析、浮层怎么画。

## 公开接口
- `ISessionWatcher`：`Current`、`SnapshotChanged`、`Availability`、`TryPlayPauseAsync` / `TryNextAsync` / `TryPreviousAsync`、`TryGetThumbnailAsync`。
- `MediaSnapshot`：曲目、艺人、专辑、时长、进度、AppId。
- `PlaybackClock`：每帧可读的有效进度（含会话/设备延迟与抖动平滑）。
- `NowPlayingService`：UI 线程上的 `Snapshot`、`Resolved`、`Clock`、`Lyrics`、`SurfaceStatus`（`Resolved` / `Lyrics` 的数据来自 [lyrics](../lyrics/README.md)）。

## 依赖
- [lyrics](../lyrics/README.md)：`ILyricsResolver`（由 `NowPlayingService` 调用）。
- [settings](../settings/README.md)：延迟、是否收录浏览器会话、应用档案。
- [shared](../shared/README.md)：空状态文案 `LyricSurfaceStatus`。

## 关键约定
- 切歌时先清空已解析歌词，再异步解析，避免上一首残留。
- 时长以 `MediaTimeline.StabilizeDuration` 消抖；浏览器会话默认不收录。
- 禁止用非官方播放器 HTTP 去「补全」SMTC。

## 当前实现
`src/LyricClone.Infrastructure/Smtc/SmtcSessionWatcher.cs`  
`src/LyricClone.Core/Playback/`  
`src/LyricClone.App/Services/NowPlayingService.cs`  
测试：`tests/LyricClone.Core.Tests/PlaybackClockTests.cs`、`tests/LyricClone.Flows.Tests/PlaybackFlowTests.cs`
