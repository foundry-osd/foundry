using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Foundry.Views;

internal static class ReleaseNotesMarkdownDocumentBuilder
{
    private static readonly Regex HeadingPattern = new(@"^(#{1,3})\s+(?<text>.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex BulletPattern = new(@"^\s*[-*]\s+(?<text>.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex MarkdownLinkPattern = new(@"\[(?<text>[^\]]+)\]\((?<url>https?://[^\s)]+)\)", RegexOptions.Compiled);
    private static readonly Regex BareUrlPattern = new(@"https?://[^\s]+", RegexOptions.Compiled);

    public static FlowDocument Build(string markdown, Action<string> openUrl)
    {
        ArgumentNullException.ThrowIfNull(openUrl);

        Brush foreground = ResolveBrush("TextFillColorPrimaryBrush", Brushes.White);
        Brush hyperlinkBrush = ResolveBrush("AccentTextFillColorPrimaryBrush", ResolveBrush("AccentFillColorDefaultBrush", Brushes.DeepSkyBlue));

        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            ColumnWidth = double.PositiveInfinity,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            Foreground = foreground
        };

        string[] lines = (markdown ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        var paragraphBuffer = new StringBuilder();
        List? currentList = null;

        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd();

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph(document, paragraphBuffer, openUrl, hyperlinkBrush);
                FlushList(document, ref currentList);
                continue;
            }

            Match headingMatch = HeadingPattern.Match(line);
            if (headingMatch.Success)
            {
                FlushParagraph(document, paragraphBuffer, openUrl, hyperlinkBrush);
                FlushList(document, ref currentList);
                document.Blocks.Add(CreateHeading(
                    headingMatch.Groups["text"].Value,
                    headingMatch.Groups[1].Value.Length,
                    openUrl,
                    hyperlinkBrush));
                continue;
            }

            Match bulletMatch = BulletPattern.Match(line);
            if (bulletMatch.Success)
            {
                FlushParagraph(document, paragraphBuffer, openUrl, hyperlinkBrush);
                currentList ??= CreateList();
                currentList.ListItems.Add(CreateListItem(
                    bulletMatch.Groups["text"].Value,
                    openUrl,
                    hyperlinkBrush));
                continue;
            }

            if (paragraphBuffer.Length > 0)
            {
                paragraphBuffer.Append(' ');
            }

            paragraphBuffer.Append(line.Trim());
        }

        FlushParagraph(document, paragraphBuffer, openUrl, hyperlinkBrush);
        FlushList(document, ref currentList);

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new Paragraph(new Run(string.Empty)));
        }

        return document;
    }

    private static void FlushParagraph(
        FlowDocument document,
        StringBuilder paragraphBuffer,
        Action<string> openUrl,
        Brush hyperlinkBrush)
    {
        if (paragraphBuffer.Length == 0)
        {
            return;
        }

        document.Blocks.Add(CreateParagraph(paragraphBuffer.ToString(), openUrl, hyperlinkBrush));
        paragraphBuffer.Clear();
    }

    private static void FlushList(FlowDocument document, ref List? currentList)
    {
        if (currentList is null)
        {
            return;
        }

        document.Blocks.Add(currentList);
        currentList = null;
    }

    private static Paragraph CreateHeading(string text, int level, Action<string> openUrl, Brush hyperlinkBrush)
    {
        double fontSize = level switch
        {
            1 => 22,
            2 => 18,
            _ => 16
        };

        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 10),
            FontWeight = FontWeights.SemiBold,
            FontSize = fontSize
        };

        AddInlines(paragraph.Inlines, text, openUrl, hyperlinkBrush);
        return paragraph;
    }

    private static Paragraph CreateParagraph(string text, Action<string> openUrl, Brush hyperlinkBrush)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 12),
            LineHeight = 22
        };

        AddInlines(paragraph.Inlines, text, openUrl, hyperlinkBrush);
        return paragraph;
    }

    private static List CreateList()
    {
        return new List
        {
            MarkerStyle = TextMarkerStyle.Disc,
            Margin = new Thickness(18, 0, 0, 12)
        };
    }

    private static ListItem CreateListItem(string text, Action<string> openUrl, Brush hyperlinkBrush)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 6),
            LineHeight = 22
        };

        AddInlines(paragraph.Inlines, text, openUrl, hyperlinkBrush);
        return new ListItem(paragraph);
    }

    private static void AddInlines(
        InlineCollection inlines,
        string text,
        Action<string> openUrl,
        Brush hyperlinkBrush)
    {
        int index = 0;

        while (index < text.Length)
        {
            if (TryAddBold(inlines, text, ref index, openUrl, hyperlinkBrush))
            {
                continue;
            }

            Match markdownLinkMatch = MarkdownLinkPattern.Match(text, index);
            if (markdownLinkMatch.Success && markdownLinkMatch.Index == index)
            {
                inlines.Add(CreateHyperlink(
                    markdownLinkMatch.Groups["text"].Value,
                    markdownLinkMatch.Groups["url"].Value,
                    openUrl,
                    hyperlinkBrush));
                index += markdownLinkMatch.Length;
                continue;
            }

            Match bareUrlMatch = BareUrlPattern.Match(text, index);
            if (bareUrlMatch.Success && bareUrlMatch.Index == index)
            {
                string url = TrimTrailingPunctuation(bareUrlMatch.Value);
                inlines.Add(CreateHyperlink(url, url, openUrl, hyperlinkBrush));
                index += url.Length;
                continue;
            }

            int nextSpecialIndex = FindNextSpecialIndex(text, index);
            int length = (nextSpecialIndex >= 0 ? nextSpecialIndex : text.Length) - index;
            if (length <= 0)
            {
                break;
            }

            inlines.Add(new Run(text.Substring(index, length)));
            index += length;
        }
    }

    private static bool TryAddBold(
        InlineCollection inlines,
        string text,
        ref int index,
        Action<string> openUrl,
        Brush hyperlinkBrush)
    {
        if (!text.AsSpan(index).StartsWith("**", StringComparison.Ordinal))
        {
            return false;
        }

        int closingIndex = text.IndexOf("**", index + 2, StringComparison.Ordinal);
        if (closingIndex < 0)
        {
            return false;
        }

        string boldText = text.Substring(index + 2, closingIndex - (index + 2));
        var bold = new Bold();
        AddInlines(bold.Inlines, boldText, openUrl, hyperlinkBrush);
        inlines.Add(bold);
        index = closingIndex + 2;
        return true;
    }

    private static int FindNextSpecialIndex(string text, int startIndex)
    {
        int[] indices =
        [
            text.IndexOf("**", startIndex, StringComparison.Ordinal),
            text.IndexOf('[', startIndex),
            text.IndexOf("http://", startIndex, StringComparison.Ordinal),
            text.IndexOf("https://", startIndex, StringComparison.Ordinal)
        ];

        return indices
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();
    }

    private static Hyperlink CreateHyperlink(
        string displayText,
        string url,
        Action<string> openUrl,
        Brush hyperlinkBrush)
    {
        var hyperlink = new Hyperlink(new Run(displayText))
        {
            Foreground = hyperlinkBrush
        };

        hyperlink.Click += (_, _) => openUrl(url);
        return hyperlink;
    }

    private static string TrimTrailingPunctuation(string value)
    {
        return value.TrimEnd('.', ',', ';', ':', ')');
    }

    private static Brush ResolveBrush(string resourceKey, Brush fallback)
    {
        return Application.Current?.TryFindResource(resourceKey) as Brush ?? fallback;
    }
}
