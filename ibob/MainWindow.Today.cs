using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using Ical.Net.CalendarComponents;
using Microsoft.Win32;

namespace Ibob;

// One upcoming event from your calendar. Link is a Teams / Zoom / Google Meet join link, if the event has one.
public record Meeting(DateTime Start, DateTime End, string Title, string Place, string Link, bool AllDay)
{
    public string When => AllDay ? "All day" : Start.Date == DateTime.Today ? Start.ToString("h:mm") : Start.ToString("ddd h:mm");

    // "now", "in 25 min", "in 3 h", or the day, followed by the room/place if there is one.
    public string Detail
    {
        get
        {
            var wait = Start - DateTime.Now;
            string time = AllDay ? Start.ToString("dddd") : wait <= TimeSpan.Zero ? "now" : wait.TotalMinutes < 60 ? $"in {Math.Ceiling(wait.TotalMinutes)} min" : wait.TotalHours < 24 ? $"in {Math.Floor(wait.TotalHours)} h" : Start.ToString("dddd");
            return Place.Length > 0 ? $"{time} · {Place}" : time;
        }
    }

    public Visibility JoinVisible => Link == null ? Visibility.Collapsed : Visibility.Visible;
}

// Today page: your next events with a Join button, a heads-up 10 minutes before each one,
// and dots in the pill while your camera (green) or microphone (orange) is in use.
public partial class MainWindow
{
    static readonly string CalendarsFile = Path.Combine(App.DataFolder, "calendars.txt");
    static readonly Regex JoinLink = new(@"https://(teams\.microsoft\.com/l/meetup-join|[\w.-]*zoom\.us/(j|my)|meet\.google\.com)/[^\s""<>]+");
    static readonly HttpClient Web = new() { Timeout = TimeSpan.FromSeconds(20) };
    static readonly TimeSpan RemindBefore = TimeSpan.FromMinutes(10), CalendarRefresh = TimeSpan.FromMinutes(15);

    List<Meeting> meetings = new();
    readonly HashSet<string> reminded = new();
    DateTime calendarLoaded;
    bool cameraOn, micInUse;

    // ---------- Calendar ----------

    List<string> CalendarLinks() =>
        File.Exists(CalendarsFile) ? File.ReadAllLines(CalendarsFile).Select(l => l.Trim()).Where(l => l.Length > 0).ToList() : new();

    // Downloads each calendar link (or reads a local .ics file) and keeps the next 7 days of events.
    async void LoadCalendars()
    {
        calendarLoaded = DateTime.Now;
        var links = CalendarLinks();
        var found = new List<Meeting>();
        string problem = "";
        foreach (var link in links)
        {
            try
            {
                string ics = link.StartsWith("http") || link.StartsWith("webcal")
                    ? await Web.GetStringAsync(Regex.Replace(link, "^webcal://", "https://"))
                    : await File.ReadAllTextAsync(link);
                found.AddRange(ReadCalendar(ics));
            }
            catch (Exception e) { problem = "Couldn't load a calendar: " + e.Message; }
        }
        meetings = found.OrderBy(m => m.Start).ToList();
        TodayStatus.Text = problem;
        ShowToday();
    }

    static IEnumerable<Meeting> ReadCalendar(string ics)
    {
        var calendar = Ical.Net.Calendar.Load(ics);
        foreach (var occurrence in calendar.GetOccurrences(DateTime.Today, DateTime.Today.AddDays(7)))
        {
            if (occurrence.Source is not CalendarEvent e) continue;
            var start = occurrence.Period.StartTime.AsSystemLocal;
            var end = occurrence.Period.EndTime?.AsSystemLocal ?? start.Add(e.Duration);
            string link = JoinLink.Match($"{e.Location} {e.Description} {e.Url}") is { Success: true } m ? m.Value : null;
            yield return new Meeting(start, end, e.Summary ?? "(no title)", e.Location ?? "", link, e.IsAllDay);
        }
    }

    // The next two events that haven't ended yet, shown above the tasks on the Plan tab.
    // Calendar links are added in Settings; with none (or nothing coming up) this area simply stays empty.
    void ShowToday()
    {
        MeetingList.ItemsSource = meetings.Where(m => m.End > DateTime.Now).Take(2).ToList();
        TodayStatus.Visibility = TodayStatus.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void JoinClicked(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(((Meeting)((FrameworkElement)sender).DataContext).Link) { UseShellExecute = true });

    // Runs every second: refresh the feed now and then, warn before events, update the camera/mic dots.
    void CheckToday()
    {
        if (DateTime.Now - calendarLoaded > CalendarRefresh) LoadCalendars();

        var now = DateTime.Now;
        foreach (var m in meetings.Where(m => !m.AllDay && m.Start > now && m.Start - now <= RemindBefore))
        {
            if (!reminded.Add(m.Title + m.Start)) continue;
            Chime();
            ShowAlert(null, $"{m.Title} in {Math.Ceiling((m.Start - now).TotalMinutes)} min", PlanIndex);
        }
        if (isOpen && page == PlanIndex && now.Second == 0) ShowToday();   // keep "in 25 min" current

        cameraOn = InUse("webcam");
        micInUse = InUse("microphone");
        CameraDot.Visibility = cameraOn ? Visibility.Visible : Visibility.Collapsed;
        MicDot.Visibility = micInUse ? Visibility.Visible : Visibility.Collapsed;
    }

    // True while a meeting is about to start or your camera/mic is in use: hovering then opens Plan first.
    bool MeetingSoon => cameraOn || micInUse || meetings.Any(m => !m.AllDay && m.Start - DateTime.Now <= TimeSpan.FromMinutes(15) && m.End > DateTime.Now);

    // Windows records, per app, when it last started and stopped using the camera or mic.
    // "Started but not stopped" (stop time 0) means it's in use right now.
    static bool InUse(string device)
    {
        using var store = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{device}");
        return store != null && AnyAppUsing(store);
    }

    static bool AnyAppUsing(RegistryKey key)
    {
        foreach (var name in key.GetSubKeyNames())
        {
            using var app = key.OpenSubKey(name);
            if (app == null) continue;
            if (app.GetValue("LastUsedTimeStart") is long started && started > 0 && app.GetValue("LastUsedTimeStop") is long stopped && stopped == 0) return true;
            if (name == "NonPackaged" && AnyAppUsing(app)) return true;   // regular desktop apps live one level down
        }
        return false;
    }
}