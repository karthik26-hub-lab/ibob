using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Ibob;

// Tasks page (to-do with due-date countdowns and reminders) and Notes page (a notepad that saves as you type).
public partial class MainWindow
{
    static readonly string TasksFile = Path.Combine(App.DataFolder, "tasks.json");
    static readonly string NotesFile = Path.Combine(App.DataFolder, "notes.txt");
    static readonly TimeSpan HeadsUp = TimeSpan.FromHours(1);

    List<TaskItem> tasks = new();
    readonly HashSet<string> taskReminders = new();
    readonly DispatcherTimer notesSave = new() { Interval = TimeSpan.FromMilliseconds(800) };

    void LoadTasksAndNotes()
    {
        try { tasks = JsonSerializer.Deserialize<List<TaskItem>>(File.ReadAllText(TasksFile)) ?? new(); }
        catch { tasks = new(); }   // no file yet
        ShowTasks();

        NotesBox.Text = File.Exists(NotesFile) ? File.ReadAllText(NotesFile) : "";
        notesSave.Tick += (_, _) => SaveNotes();
        ShowNotesHint();
    }

    // ---------- Tasks ----------

    void SaveTasks()
    {
        Directory.CreateDirectory(App.DataFolder);
        File.WriteAllText(TasksFile, JsonSerializer.Serialize(tasks));
    }

    // Open tasks first (soonest due first, undated after), finished ones at the bottom.
    void ShowTasks()
    {
        TaskList.ItemsSource = tasks
            .OrderBy(t => t.Done)
            .ThenBy(t => t.Due ?? DateTime.MaxValue)
            .ThenBy(t => t.Created)
            .ToList();
        NoTasks.Visibility = tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Enter adds the task; a date at the end ("fri 5pm") becomes its due time.
    void NewTaskKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || NewTask.Text.Trim().Length == 0) return;
        var (text, due) = TaskParser.Parse(NewTask.Text, DateTime.Now);
        tasks.Add(new TaskItem { Text = text, Due = due, Created = DateTime.Now });
        NewTask.Clear();
        SaveTasks();
        ShowTasks();
        e.Handled = true;
    }

    void NewTaskChanged(object sender, TextChangedEventArgs e) =>
        TaskHint.Visibility = NewTask.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    void TaskToggle(object sender, MouseButtonEventArgs e)
    {
        var task = (TaskItem)((FrameworkElement)sender).DataContext;
        task.Done = !task.Done;
        SaveTasks();
        ShowTasks();
    }

    void TaskDelete(object sender, MouseButtonEventArgs e)
    {
        tasks.Remove((TaskItem)((FrameworkElement)sender).DataContext);
        SaveTasks();
        ShowTasks();
    }

    void ClearDoneTasks(object sender, RoutedEventArgs e)
    {
        tasks.RemoveAll(t => t.Done);
        SaveTasks();
        ShowTasks();
    }

    // Runs every second: a heads-up 1 hour before a task is due and again when it's due; refreshes countdowns each minute.
    void CheckTasks()
    {
        var now = DateTime.Now;
        foreach (var task in tasks.Where(t => !t.Done && t.Due != null))
        {
            var left = task.Due.Value - now;
            if (left <= HeadsUp && left > HeadsUp - TimeSpan.FromMinutes(1) && taskReminders.Add("hour" + task.Created))
                Remind($"{task.Text} · due in 1 hour");
            else if (left <= TimeSpan.Zero && left > TimeSpan.FromMinutes(-1) && taskReminders.Add("due" + task.Created))
                Remind($"{task.Text} · due now");
        }
        if (isOpen && page == TasksIndex && now.Second == 0) ShowTasks();
    }

    void Remind(string text)
    {
        Chime();
        ShowAlert(null, text, TasksIndex);
    }

    // ---------- Notes ----------

    // Saves shortly after you stop typing, so it never loses more than a moment of writing.
    void NotesChanged(object sender, TextChangedEventArgs e)
    {
        ShowNotesHint();
        notesSave.Stop();
        notesSave.Start();
    }

    void SaveNotes()
    {
        notesSave.Stop();
        Directory.CreateDirectory(App.DataFolder);
        File.WriteAllText(NotesFile, NotesBox.Text);
        NotesStatus.Text = "Saved";
    }

    void ShowNotesHint() => NotesHint.Visibility = NotesBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    void CopyNotes(object sender, RoutedEventArgs e)
    {
        if (NotesBox.Text.Length == 0) return;
        ignoreClipboardUntil = DateTime.Now.AddSeconds(1);
        Clipboard.SetText(NotesBox.Text);
        NotesStatus.Text = "Copied";
    }

    // Saves a copy of the notes as a text file on the shelf (to drag into an email, chat or folder).
    void NotesToShelf(object sender, RoutedEventArgs e)
    {
        if (NotesBox.Text.Trim().Length == 0) return;
        Directory.CreateDirectory(ShelfFolder);
        AddToShelf(SaveText(NotesBox.Text));
        SaveShelf();
        NotesStatus.Text = "Added to shelf";
    }
}
