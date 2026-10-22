# stage

## 职责
负责舞台歌词：居中大字、封面取色渐变。Esc / F11 / 双击退出。

不负责：取歌词、系统任务栏。

## 公开接口
- `StageWindow`
- `StageGradient`：封面采样亮度上限。

## 依赖
- [playback](../playback/README.md)
- [settings](../settings/README.md)：`AppSettings.Stage`
- [shared](../shared/README.md)

## 当前实现
`src/LyricClone.App/Views/StageWindow.*`  
`src/LyricClone.Core/Views/StageGradient.cs`
