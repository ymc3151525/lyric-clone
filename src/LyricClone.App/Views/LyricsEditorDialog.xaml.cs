using System.Windows;
using LyricClone.Core.Library;
using LyricClone.Core.Lyrics;
using LyricClone.Core.Settings;

namespace LyricClone.App.Views;

public sealed record EditorResult(string Name, LyricsFormat Format, string Content, string? Translation, ParsedLyrics Parsed);

/// <summary>S13: add or edit a user lyrics version, with a live parse check.</summary>
public partial class LyricsEditorDialog : Window
{
    private readonly ILyricsParser _parser;
    private bool _ready;

    public LyricsEditorDialog(ILyricsParser parser, string name, string content, LyricsVersion? existing)
    {
        InitializeComponent();
        _parser = parser;
        Title = existing is null
            ? Loc.T("添加本地歌词", "Add local lyrics")
            : Loc.T("编辑本地歌词", "Edit local lyrics");
        NameBox.Text = name;
        ContentBox.Text = content;
        TranslationBox.Text = existing?.Translation ?? "";
        if (existing is not null)
        {
            FormatBox.SelectedIndex = existing.Format switch
            {
                LyricsFormat.Lrc => 1,
                LyricsFormat.Ttml => 2,
                LyricsFormat.Qrc => 3,
                LyricsFormat.Krc => 4,
                LyricsFormat.Yrc => 5,
                LyricsFormat.LyricifyLines => 6,
                LyricsFormat.LyricifySyllable => 7,
                LyricsFormat.Plain => 8,
                _ => 0,
            };
        }

        _ready = true;
        Loaded += (_, _) => ContentBox.Focus();
        Validate();
    }

    public EditorResult? Result { get; private set; }

    private LyricsFormat SelectedFormat => FormatBox.SelectedIndex switch
    {
        1 => LyricsFormat.Lrc,
        2 => LyricsFormat.Ttml,
        3 => LyricsFormat.Qrc,
        4 => LyricsFormat.Krc,
        5 => LyricsFormat.Yrc,
        6 => LyricsFormat.LyricifyLines,
        7 => LyricsFormat.LyricifySyllable,
        8 => LyricsFormat.Plain,
        _ => _parser.Detect(ContentBox.Text),
    };

    public static LyricsFormat Detect(string content) => new LyricsParser().Detect(content);

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_ready)
        {
            Validate();
        }
    }

    private ParsedLyrics? Validate()
    {
        var content = ContentBox.Text;
        if (string.IsNullOrWhiteSpace(content))
        {
            StatusText.Text = Loc.T("请粘贴或输入歌词。", "Paste or type lyrics.");
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.TextMuted");
            SaveButton.IsEnabled = false;
            return null;
        }

        var format = SelectedFormat;
        var translation = string.IsNullOrWhiteSpace(TranslationBox.Text) ? null : TranslationBox.Text;
        ParsedLyrics parsed;
        try
        {
            parsed = _parser.Parse(content, format, translation);
        }
        catch (NotSupportedException)
        {
            parsed = ParsedLyrics.Empty;
        }

        if (parsed.IsEmpty)
        {
            StatusText.Text = format == LyricsFormat.Ttml
                ? Loc.T("无法解析：TTML 内容不是有效的 XML，或没有带时间的行。", "Can't parse: the TTML isn't valid XML, or has no timed lines.")
                : Loc.T("无法解析：没有找到任何歌词行。", "Can't parse: no lyric lines were found.");
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger");
            SaveButton.IsEnabled = false;
            return null;
        }

        var kind = parsed.HasWordSync
            ? Loc.T("逐字", "word-synced")
            : parsed.IsSynced ? Loc.T("逐行", "line-synced") : Loc.T("无时间轴", "unsynced");
        var detected = FormatBox.SelectedIndex == 0
            ? Loc.T($"识别为 {FormatName(format)}，", $"Detected as {FormatName(format)}, ")
            : "";
        var tr = parsed.HasTranslation
            ? Loc.T($"，{parsed.Lines.Count(l => l.Translation is not null)} 行带翻译", $", {parsed.Lines.Count(l => l.Translation is not null)} translated")
            : "";
        StatusText.Text = Loc.T($"{detected}共 {parsed.Lines.Count} 行，{kind}{tr}。", $"{detected}{parsed.Lines.Count} lines, {kind}{tr}.");
        StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Success");
        SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(NameBox.Text);
        return parsed;
    }

    private static string FormatName(LyricsFormat f) => f switch
    {
        LyricsFormat.Lrc => "LRC",
        LyricsFormat.Ttml => "TTML",
        LyricsFormat.Qrc => "QRC",
        LyricsFormat.Krc => "KRC",
        LyricsFormat.Yrc => "YRC",
        LyricsFormat.LyricifyLines => "Lyricify Lines",
        LyricsFormat.LyricifySyllable => "Lyricify Syllable",
        _ => Loc.T("纯文本", "plain text"),
    };

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (Validate() is not { } parsed)
        {
            return;
        }

        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            NameBox.Focus();
            return;
        }

        Result = new EditorResult(
            name,
            SelectedFormat,
            ContentBox.Text,
            string.IsNullOrWhiteSpace(TranslationBox.Text) ? null : TranslationBox.Text,
            parsed);
        DialogResult = true;
    }
}
