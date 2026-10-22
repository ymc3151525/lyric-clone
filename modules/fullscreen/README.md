# fullscreen

## 职责
负责全屏歌词：当前行锚定、模糊封面背景。Esc 退出。

不负责：取歌词、舞台渐变（见 [stage](../stage/README.md)）。

## 公开接口
- `FullscreenLyricsWindow`

## 依赖
- [playback](../playback/README.md)
- [settings](../settings/README.md)：`AppSettings.Fullscreen`
- [shared](../shared/README.md)：`LyricLineView`、`CoverSampler`

## 关键约定
- Esc / 托盘开关关闭；可被灵动岛「全屏时隐藏」设置影响（由 island/settings 读取，不反向依赖）。

## 当前实现
`src/LyricClone.App/Views/FullscreenLyricsWindow.*`
