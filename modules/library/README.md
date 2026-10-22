# library

## 职责
负责本地曲目库（SQLite）以及「当前播放 / 全部曲目」管理窗：钉选、确认、导入、编辑、偏移、按 LRCLIB 搜索或 ID 指定。

不负责：自动从播放器缓存/网络拉取的策略（见 [lyrics](../lyrics/README.md)），也不画桌面/灵动岛。

## 公开接口
- `ILibraryRepository`：`GetOrCreateTrackAsync`、`QueryTracksAsync`、`GetVersionsAsync`、`SaveCandidateAsync`、`AddUserVersionAsync`、钉选/确认/偏移/删除。
- `ManagerWindow` / `ManagerViewModel`：当前播放（`allTracks: false`）与全部曲目。
- 对话框：`SearchDialog`、`ByIdDialog`、`LyricsEditorDialog`、`OffsetDialog`。

数据文件：`%AppData%\lyric-clone\library.db`（可用 `LYRIC_CLONE_DATA_DIR` 覆盖）。

## 依赖
- [lyrics](../lyrics/README.md)：`ILyricsResolver`、`ILyricsParser`。
- [playback](../playback/README.md)：当前曲目（仅当前播放窗）。
- [settings](../settings/README.md)：来源顺序。
- [shared](../shared/README.md)：文案。

## 关键约定
- 一轨最多一个 `pinned`；同一在线来源只保留一个版本。
- 用户导入的歌词算 `user` 来源，不覆盖自动缓存来源。
- 管理窗改版本后通过 `NowPlayingService.ReloadCurrentAsync` 刷新正在显示的歌词。

## 当前实现
`src/LyricClone.Infrastructure/Data/`  
`src/LyricClone.App/Views/ManagerWindow.*`、`ViewModels/ManagerViewModel.cs`  
`SearchDialog`、`ByIdDialog`、`LyricsEditorDialog`、`OffsetDialog`  
测试：`tests/LyricClone.Infrastructure.Tests/Library*.cs`、`tests/LyricClone.Flows.Tests/ManagerFlowTests.cs`
