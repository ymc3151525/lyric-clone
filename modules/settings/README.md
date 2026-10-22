# settings

## 职责
负责设置窗（常规 / 歌词 / 视图 / 应用 / 关于）以及 `%AppData%\lyric-clone\settings.json` 的读写、导入导出。

不负责：各浮层自己的绘制；不在这里发 LRCLIB 请求。

## 公开接口
- `ISettingsStore`：`Current`、`Changed`、`UpdateAsync`、`ExportAsync`、`ImportAsync`。
- `AppSettings` 及嵌套的 Desktop / Island / Taskbar / Fullscreen / Stage / 热键 / 应用档案。
- `SettingsWindow`：由 [shell](../shell/README.md) 打开。
- `AppSettings.EffectiveSourceOrder(appId)`：全局顺序 + 应用覆盖，并插入播放器缓存来源。

## 依赖
- [shared](../shared/README.md)：文案、颜色校验 `ColorHex`。
- 各视图模块：只通过 `AppSettings` 的字段消费，不引用本模块窗口内部控件。

## 关键约定
- JSON 原子保存；坏文件备份后回退默认。
- 关于页必须保留 Lyricify CC BY-SA 4.0 致谢与 Helper Apache-2.0 声明。
- 不提供 QQ/网易/酷狗在线源开关。

## 当前实现
`src/LyricClone.Infrastructure/Settings/SettingsStore.cs`  
`src/LyricClone.Core/Settings/`  
`src/LyricClone.App/Views/SettingsWindow.*`  
测试：`tests/LyricClone.Core.Tests/SettingsLogicTests.cs`、`tests/LyricClone.Infrastructure.Tests/SettingsStoreTests.cs`
