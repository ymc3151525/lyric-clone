# island

## 职责
负责灵动岛浮层：贴边形状、宽度随歌词、长句来回滚动、封面、翻译行。

不负责：取歌词、SMTC 控制条（岛上不放播放按钮和歌名）。形态向 Lyricify（WXRIW）公开设计致谢，CC BY-SA 4.0。

## 公开接口
- `IslandWindow`：由 [shell](../shell/README.md) 创建；绑定 `NowPlayingService`。
- `IslandPlacement`、`IslandChrome`、`IslandFit`：贴边、耳朵、宽度与滚动。

## 依赖
- [playback](../playback/README.md)：`NowPlayingService`。
- [settings](../settings/README.md)：`AppSettings.Island`。
- [shared](../shared/README.md)：`LyricLineView`、主题 token。

## 关键约定
- 封面固定约 36px，不随歌词区撑高。
- 有翻译时只显示当前行+译文，不叠下一句。
- 右贴边时右边贴齐，左侧随宽度变化。
- 鼠标划过可隐藏（`HideFromMouse`）。

## 当前实现
`src/LyricClone.App/Views/IslandWindow.*`  
`src/LyricClone.Core/Views/IslandFit.cs`、`IslandChrome.cs`、`IslandPlacement.cs`  
测试：`tests/LyricClone.Core.Tests/IslandPlacementTests.cs`、`tests/LyricClone.Flows.Tests/ViewsFlowTests.cs`（F09 island）
