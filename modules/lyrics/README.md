# lyrics

## 职责
负责按来源顺序取歌词、解析格式、匹配曲目、英文歌词配对中文翻译。

不负责：SMTC、曲目库 UI、各浮层布局。版本的存盘走 [library](../library/README.md) 的公开接口。

## 公开接口
- `ILyricsResolver`：`ResolveAsync`、`ReloadAsync`、`Parse`、`FetchAndSaveAsync`、`SaveCandidateAsync`。
- `ILyricsParser` / `HelperLyricsParser`：`Detect`、`Parse`（LRC / TTML / QRC / KRC / YRC 等）。
- `ResolvedLyrics`、`ParsedLyrics`、`LyricPresenter.Apply`（展示前的简繁/罗马音/过滤；翻译总开关）。
- 来源 id：`LyricsSources`（`qq_cache` / `netease_cache` / `kugou_cache` / `local_file` / `embedded` / `lrclib`）。

## 依赖
- [library](../library/README.md)：`ILibraryRepository` 存版本、记未命中。
- [playback](../playback/README.md)：`MediaSnapshot` / `Track` 元数据。
- [settings](../settings/README.md)：来源顺序、本地歌词目录、是否自动拉取。

## 关键约定
- 只读本机播放器缓存、嵌入标签、本地文件、官方 LRCLIB；**禁止**非官方 QQ/网易/酷狗 HTTP。
- LRCLIB：User-Agent、一次一个请求、遵守 Retry-After。
- 仅当主歌词是英文时才附加中文翻译。
- 测试与仓库不得包含真实歌词。
- QQ `.qrc` 可能是 QRC，也可能是套了 XML 的 LRC；解析前要抽出 `LyricContent`。

## 当前实现
`src/LyricClone.Infrastructure/Lyrics/`、`Lrclib/`  
`src/LyricClone.Core/Lyrics/`、`Library/TrackNormalizer.cs`、`Library/VersionSelector.cs`  
测试：`tests/LyricClone.Infrastructure.Tests/`、`tests/LyricClone.Core.Tests/*Parser*`
