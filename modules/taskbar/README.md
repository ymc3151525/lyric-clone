# taskbar

## 职责
负责贴在系统任务栏上的歌词条。

不负责：取歌词、改造系统任务栏（无官方扩展点，只用顶层工具窗对齐）。

## 公开接口
- `TaskbarLyricsWindow`
- `TaskbarLayout`、`ITaskbarQuery`：任务栏矩形、自动隐藏、全屏时隐藏。

## 依赖
- [playback](../playback/README.md)
- [settings](../settings/README.md)：`AppSettings.Taskbar`
- [shared](../shared/README.md)

## 关键约定
- 用 `SHAppBarMessage` 对齐；纵向任务栏 / 自动隐藏 / 全屏应用时隐藏。

## 当前实现
`src/LyricClone.App/Views/TaskbarLyricsWindow.*`  
`src/LyricClone.App/Interop/TaskbarQuery.cs`  
`src/LyricClone.Core/Views/TaskbarLayout.cs`
