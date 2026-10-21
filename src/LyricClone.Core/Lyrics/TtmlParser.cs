using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace LyricClone.Core.Lyrics;

/// <summary>
/// TTML lyrics: &lt;p begin end&gt; lines with optional timed &lt;span&gt; syllables, inline translation spans
/// (ttm:role="x-translation") and head-level translations keyed by itunes:key.
/// Background-vocal spans (ttm:role="x-bg") are folded into the line text.
/// </summary>
public static class TtmlParser
{
    private const string TtmNs = "http://www.w3.org/ns/ttml#metadata";

    public static ParsedLyrics Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return ParsedLyrics.Empty;
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return ParsedLyrics.Empty;
        }

        var headTranslations = ReadHeadTranslations(doc);
        var lines = new List<LyricLine>();
        bool synced = false;

        foreach (var p in doc.Descendants().Where(e => e.Name.LocalName == "p"))
        {
            bool hasBegin = TimeTag.TryParseTtml(Attr(p, "begin"), out long begin);
            bool hasEnd = TimeTag.TryParseTtml(Attr(p, "end"), out long end);

            var syllables = new List<Syllable>();
            var text = new StringBuilder();
            string? translation = null;

            foreach (var node in p.Nodes())
            {
                switch (node)
                {
                    case XText t:
                        AppendLoose(t.Value, text, syllables);
                        break;
                    case XElement span when span.Name.LocalName == "span":
                        var role = span.Attribute(XName.Get("role", TtmNs))?.Value;
                        if (role == "x-translation")
                        {
                            translation = Normalise(span.Value);
                            break;
                        }

                        if (role == "x-roman")
                        {
                            break;
                        }

                        ReadSpan(span, text, syllables);
                        break;
                    case XElement br when br.Name.LocalName == "br":
                        text.Append(' ');
                        break;
                }
            }

            if (translation is null)
            {
                var key = p.Attributes().FirstOrDefault(a => a.Name.LocalName == "key")?.Value;
                if (key is not null && headTranslations.TryGetValue(key, out var tr))
                {
                    translation = tr;
                }
            }

            if (!hasBegin && syllables.Count > 0)
            {
                begin = syllables[0].StartMs;
                hasBegin = true;
            }

            if (!hasEnd && syllables.Count > 0)
            {
                end = syllables[^1].EndMs;
                hasEnd = true;
            }

            synced |= hasBegin;
            var lineText = Normalise(text.ToString());
            lines.Add(new LyricLine(begin, hasEnd ? Math.Max(end, begin) : begin, lineText, syllables, translation));
        }

        if (synced)
        {
            lines.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].EndMs <= lines[i].StartMs)
                {
                    long next = i + 1 < lines.Count ? lines[i + 1].StartMs : lines[i].StartMs + 5000;
                    lines[i] = lines[i] with { EndMs = Math.Max(next, lines[i].StartMs + 1) };
                }
            }
        }

        return new ParsedLyrics(lines, synced);
    }

    private static void ReadSpan(XElement span, StringBuilder text, List<Syllable> syllables)
    {
        bool timed = TimeTag.TryParseTtml(Attr(span, "begin"), out long b) & TimeTag.TryParseTtml(Attr(span, "end"), out long e);
        if (timed && !span.Elements().Any())
        {
            var value = span.Value;
            text.Append(value);
            if (value.Length > 0)
            {
                syllables.Add(new Syllable(b, Math.Max(0, e - b), value));
            }

            return;
        }

        foreach (var node in span.Nodes())
        {
            switch (node)
            {
                case XText t:
                    AppendLoose(t.Value, text, syllables);
                    break;
                case XElement child when child.Name.LocalName == "span":
                    var role = child.Attribute(XName.Get("role", TtmNs))?.Value;
                    if (role is "x-translation" or "x-roman")
                    {
                        break;
                    }

                    ReadSpan(child, text, syllables);
                    break;
            }
        }
    }

    /// <summary>Untimed text between syllables (usually a space) belongs to the previous syllable.</summary>
    private static void AppendLoose(string value, StringBuilder text, List<Syllable> syllables)
    {
        if (value.Length == 0)
        {
            return;
        }

        bool isWhitespace = string.IsNullOrWhiteSpace(value);
        if (isWhitespace && (value.Contains('\n') || value.Contains('\r')))
        {
            return;
        }

        var piece = isWhitespace ? " " : value;
        text.Append(piece);
        if (syllables.Count > 0)
        {
            var last = syllables[^1];
            syllables[^1] = last with { Text = last.Text + piece };
        }
    }

    private static Dictionary<string, string> ReadHeadTranslations(XDocument doc)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var translation = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "translation");
        if (translation is null)
        {
            return map;
        }

        foreach (var t in translation.Elements().Where(e => e.Name.LocalName == "text"))
        {
            var key = t.Attribute("for")?.Value;
            if (key is not null)
            {
                map[key] = Normalise(t.Value);
            }
        }

        return map;
    }

    private static string? Attr(XElement e, string localName) =>
        e.Attributes().FirstOrDefault(a => a.Name.LocalName == localName)?.Value;

    private static string Normalise(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c))
            {
                space = sb.Length > 0;
                continue;
            }

            if (space)
            {
                sb.Append(' ');
                space = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
