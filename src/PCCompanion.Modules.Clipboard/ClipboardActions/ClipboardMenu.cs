using System;
using System.Collections.Generic;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Clipboard.ClipboardActions;

/// <summary>The menu's items, and the action each offer item runs.</summary>
public sealed record ClipboardMenuPlan(IReadOnlyList<PopupMenuItem> Items, IReadOnlyDictionary<uint, ClipboardOffer> Offers);

/// <summary>
/// Builds the menu the shortcut shows. See "Behavior" in docs/features/clipboard/clipboard-actions.md.
/// </summary>
public static class ClipboardMenu
{
    /// <summary>The ID of Settings, which opens PC Companion's settings.</summary>
    public const uint SettingsCommand = 1;

    /// <summary>How much of the copied text's first line the menu shows.</summary>
    public const int PreviewLength = 50;

    /// <summary>How much of a rule's name the menu shows.</summary>
    public const int NameLength = 60;

    private const uint NoteCommand = 2;
    private const uint FirstOffer = 100;
    private const char Ellipsis = '…';

    // Embeddings rather than isolates, which Windows menus draw as boxes.
    private const char LeftToRightEmbedding = '‪';
    private const char RightToLeftEmbedding = '‫';
    private const char PopDirectionalFormatting = '‬';

    /// <summary>
    /// The menu for what was read from the clipboard and the actions offered for its text: the
    /// start of the text, dimmed, the actions or a note that there are none, then Settings. Without
    /// text, a note says why.
    /// </summary>
    public static ClipboardMenuPlan Build(ClipboardTextResult read, IReadOnlyList<ClipboardOffer> offers)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(offers);

        var items = new List<PopupMenuItem>();
        var actions = new Dictionary<uint, ClipboardOffer>();
        var text = read.Status == ClipboardTextStatus.Text ? read.Text?.Trim() : null;
        if (string.IsNullOrEmpty(text))
        {
            items.Add(Note(read.Status switch
            {
                ClipboardTextStatus.Private => Strings.ClipboardActionsPrivate,
                ClipboardTextStatus.TooLong => Strings.ClipboardActionsTooLong,
                ClipboardTextStatus.Busy or ClipboardTextStatus.Unavailable => Strings.ClipboardActionsCantRead,
                _ => Strings.ClipboardActionsNoText,
            }));
        }
        else
        {
            items.Add(Note(Preview(text)));
            items.Add(PopupMenuItem.Separator);
            if (offers.Count == 0)
            {
                items.Add(Note(Strings.ClipboardActionsNoActions));
            }

            for (var index = 0; index < offers.Count; index++)
            {
                var offer = offers[index];
                var id = FirstOffer + (uint)index;
                actions[id] = offer;
                items.Add(new PopupMenuItem(id, Label(offer)));
            }
        }

        items.Add(PopupMenuItem.Separator);
        items.Add(new PopupMenuItem(SettingsCommand, Strings.TraySettings));
        return new ClipboardMenuPlan(items, actions);
    }

    /// <summary>
    /// The start of the copied text as the menu shows it: its first line, up to
    /// <see cref="PreviewLength"/> characters and an ellipsis, with control characters as spaces,
    /// and in the direction of its first letter, so it keeps its order in a menu laid out the other way.
    /// </summary>
    public static string Preview(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lineEnd = text.AsSpan().IndexOfAny('\r', '\n');
        var line = lineEnd < 0 ? text : text[..lineEnd];
        var shown = Shorten(line, PreviewLength, more: lineEnd >= 0);
        var visible = string.Create(shown.Length, shown, static (span, value) =>
        {
            for (var index = 0; index < value.Length; index++)
            {
                span[index] = char.IsControl(value[index]) ? ' ' : value[index];
            }
        });

        var embedding = StartsRightToLeft(visible) ? RightToLeftEmbedding : LeftToRightEmbedding;
        return embedding + PopupMenu.Escape(visible) + PopDirectionalFormatting;
    }

    /// <summary>
    /// An item's text: Open in browser for a link, or the rule's name, up to
    /// <see cref="NameLength"/> characters, with <c>&amp;</c> shown as written.
    /// </summary>
    public static string Label(ClipboardOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);

        return offer.Kind == OfferKind.OpenLink
            ? Strings.ClipboardActionsOpenInBrowser
            : PopupMenu.Escape(Shorten(offer.Name.Trim(), NameLength, more: false));
    }

    private static PopupMenuItem Note(string text) => new(NoteCommand, text) { IsEnabled = false };

    private static string Shorten(string text, int length, bool more) =>
        text.Length > length ? text[..length] + Ellipsis : more ? text + Ellipsis : text;

    // Whether the first letter is written right to left, as in Arabic and Hebrew.
    private static bool StartsRightToLeft(string text)
    {
        foreach (var c in text)
        {
            if (c is >= '֐' and <= 'ࣿ' or >= 'יִ' and <= '﷿' or >= 'ﹰ' and <= '﻿')
            {
                return true;
            }

            if (char.IsLetter(c))
            {
                return false;
            }
        }

        return false;
    }
}
