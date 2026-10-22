# shared

## 职责
唯一的共享模块。只放 **≥2 个模块**都用的展示与文案能力。

不负责：业务编排、网络、数据库。某项若只剩一个调用方，移回那个模块。

## 公开接口
- `LyricLineView`：卡拉 OK 填充、翻译行、过长来回滚动。
- `LyricPresenter`：简繁、罗马音、长句拆分、显式内容过滤、是否显示翻译。
- `LyricSurfaceStatus`：浮层空状态短句（不含原始错误）。
- `Loc` / `LocExtension`：zh-CN / en。
- 主题：`Themes/Tokens.xaml`、`Colors.*.xaml`、`Controls.xaml`、`LyricColors`。
- `CoverSampler`：封面取色（全屏/舞台/岛封面）。

## 依赖
无其它业务模块。可依赖 .NET / WPF。

## 关键约定
- 新增共享项前确认至少两个模块要用。
- 不在这里加播放器或歌词源。

## 当前实现
`src/LyricClone.App/Lyrics/LyricLineView.cs`、`LyricColors.cs`、`CoverSampler.cs`  
`src/LyricClone.App/Themes/`  
`src/LyricClone.Core/Lyrics/LyricPresenter.cs`、`LyricSurfaceStatus.cs`  
`src/LyricClone.Core/Settings/Loc.cs`
