// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Application;

/// <summary>
/// Composes dialog body text with one consistent paragraph separator.
/// </summary>
internal static class DialogText
{
    private static readonly string ParagraphSeparator = Environment.NewLine + Environment.NewLine;

    /// <summary>
    /// Joins the non-empty paragraphs with a blank line between each of them.
    /// </summary>
    /// <param name="paragraphs">Paragraphs in display order; null or empty entries are skipped.</param>
    /// <returns>The composed dialog text.</returns>
    internal static string JoinParagraphs(params string?[] paragraphs)
    {
        return string.Join(ParagraphSeparator, paragraphs.Where(paragraph => !string.IsNullOrEmpty(paragraph)));
    }
}
