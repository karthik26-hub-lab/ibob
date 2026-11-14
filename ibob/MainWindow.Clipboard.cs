using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Ibob;

// One copied text. Pinned ones stay at the top and are never trimmed away.
public class Clip
{
    public string Text { get; set; }
    public bool Pinned { get; set; }
    public DateTime Time { get; set; }

    // What the list shows: the first line, and a filled or outline star.
    public string Preview => Text.Trim().Split('\n')[0].Trim();
    public string Star => Pinned ? "" : "";
}

// Clipboard history: every text you copy, searchable, with pins for things you reuse.
public partial class MainWindow
{
    const int HistoryLimit = 200, VisibleClips = 4;
    static readonly string HistoryFile = Path.Combine(App.DataFolder, "history.json");

    List<Clip> history = new();

    void LoadHistory()
    {
        try { history = JsonSerializer.Deserialize<List<Clip>>(File.ReadAllText(HistoryFile)) ?? new(); }
        catch { history = new(); }   // no file yet, or unreadable: start fresh
        ShowClips();
    }

    void SaveHistory()
    {
        Directory.CreateDirectory(App.DataFolder);
        File.WriteAllText(HistoryFile, JsonSerializer.Serialize(history));
    }

    // Newest copy goes first; copying the same text again just moves it up.
    void Remember(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var existing = history.FirstOrDefault(clip => clip.Text == text);
        history.Remove(existing);
        history.Insert(0, new Clip { Text = text, Pinned = existing?.Pinned ?? false, Time = DateTime.Now });

        // Keep at most HistoryLimit unpinned clips.
        var extra = history.Where(clip => !clip.Pinned).Skip(HistoryLimit).ToList();
        history.RemoveAll(extra.Contains);
        SaveHistory();
        ShowClips();
    }

    // Pinned first, then newest; the search box filters by any words typed (in any order).
    void ShowClips()
    {
        var words = Search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = history
            .Where(clip => words.All(word => clip.Text.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(clip => clip.Pinned)
            .Take(VisibleClips)
            .ToList();

        ClipList.ItemsSource = matches;
        SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoClips.Text = history.Count == 0 ? "Text you copy shows up here" : "Nothing matches";
        NoClips.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void SearchChanged(object sender, TextChangedEventArgs e) => ShowClips();

    // Enter copies the top result; Esc clears the search.
    void SearchKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ClipList.Items.Count > 0) CopyClip((Clip)ClipList.Items[0]);
        else if (e.Key == Key.Escape) Search.Clear();
        else return;
        e.Handled = true;
    }

    void ClipClicked(object sender, MouseButtonEventArgs e) => CopyClip((Clip)((FrameworkElement)sender).DataContext);

    void CopyClip(Clip clip)
    {
        ignoreClipboardUntil = DateTime.Now.AddSeconds(1);   // don't record our own copy again
        Clipboard.SetText(clip.Text);
        ClipStatus.Text = "Copied";
    }

    void PinClicked(object sender, MouseButtonEventArgs e)
    {
        var clip = (Clip)((FrameworkElement)sender).DataContext;
        clip.Pinned = !clip.Pinned;
        SaveHistory();
        ShowClips();
        e.Handled = true;   // don't also copy it
    }

    void CopyNewUuid(object sender, RoutedEventArgs e)
    {
        string uuid = TextTools.NewUuid();
        ignoreClipboardUntil = DateTime.Now.AddSeconds(1);
        Clipboard.SetText(uuid);
        Remember(uuid);
        ClipStatus.Text = "New UUID copied";
    }

    // Clears everything except pinned clips.
    void ClearHistory(object sender, RoutedEventArgs e)
    {
        history.RemoveAll(clip => !clip.Pinned);
        SaveHistory();
        ShowClips();
    }
}
