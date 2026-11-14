using System;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace Ibob;

// One to-do item. Due is optional; when set, the list shows a live countdown.
public class TaskItem
{
    static readonly Brush Overdue = new SolidColorBrush(Color.FromRgb(255, 69, 58));
    static readonly Brush Soon = new SolidColorBrush(Color.FromRgb(255, 159, 10));
    static readonly Brush Later = new SolidColorBrush(Color.FromRgb(142, 142, 147));

    public string Text { get; set; }
    public DateTime? Due { get; set; }
    public bool Done { get; set; }
    public DateTime Created { get; set; }

    // What the list shows (not saved).
    [JsonIgnore] public string Check => Done ? "" : "";   // ticked circle / empty circle
    [JsonIgnore] public double Fade => Done ? 0.45 : 1;
    [JsonIgnore] public TextDecorationCollection Strike => Done ? TextDecorations.Strikethrough : null;
    [JsonIgnore] public string DueDate => Due?.ToString("ddd d MMM, h:mm tt");

    // "overdue", "45m", "3h 20m", "2d 4h"
    [JsonIgnore]
    public string DueText
    {
        get
        {
            if (Due is not DateTime due || Done) return "";
            var left = due - DateTime.Now;
            return left < TimeSpan.Zero ? "overdue"
                : left.TotalHours < 1 ? $"{(int)left.TotalMinutes}m"
                : left.TotalDays < 1 ? $"{(int)left.TotalHours}h {left.Minutes}m"
                : $"{(int)left.TotalDays}d {left.Hours}h";
        }
    }

    // Red when overdue, orange within a day, grey otherwise.
    [JsonIgnore]
    public Brush DueColor => Due is not DateTime due ? Later
        : due < DateTime.Now ? Overdue
        : due - DateTime.Now < TimeSpan.FromDays(1) ? Soon
        : Later;
}

// Finds a due date written at the end of a task, the way people type it:
// "DBMS assignment fri 5pm", "call mom tomorrow", "pay fees by 20 nov", "standup 9:30am", "submit on 25/11 at 11pm".
static class TaskParser
{
    const string Days = "today|tonight|tomorrow|tmrw|monday|mon|tuesday|tues|tue|wednesday|wed|thursday|thurs|thur|thu|friday|fri|saturday|sat|sunday|sun";
    const string Months = "january|jan|february|feb|march|mar|april|apr|may|june|jun|july|jul|august|aug|september|sept|sep|october|oct|november|nov|december|dec";

    static readonly Regex When = new(
        $@"\s+(?:(?:by|on|due)\s+)?(?<day>{Days}|\d{{1,2}}[/-]\d{{1,2}}|\d{{1,2}}\s+(?:{Months}))?" +
        @"(?:\s*(?:at\s+|by\s+)?(?<time>\d{1,2}(?::\d{2})?\s*(?:am|pm)|\d{1,2}:\d{2}))?\s*$",
        RegexOptions.IgnoreCase);

    // Returns the task text without the date words, and the due time (or null if none was found).
    public static (string Text, DateTime? Due) Parse(string input, DateTime now)
    {
        input = input.Trim();
        var match = When.Match(input);
        bool hasDay = match.Groups["day"].Success, hasTime = match.Groups["time"].Success;
        if (!match.Success || match.Index == 0 || (!hasDay && !hasTime)) return (input, null);

        string dayWord = match.Groups["day"].Value.ToLowerInvariant();
        DateTime? day = hasDay ? Day(dayWord, now) : now.Date;
        TimeSpan? time = hasTime ? Time(match.Groups["time"].Value) : dayWord == "tonight" ? new TimeSpan(21, 0, 0) : new TimeSpan(23, 59, 0);
        if (day == null || time == null) return (input, null);   // e.g. "31/2" or "25pm": not a real date, keep the text as is

        var due = day.Value + time.Value;
        if (!hasDay && due < now) due = due.AddDays(1);   // "5pm" typed after 5pm means tomorrow
        return (input[..match.Index].Trim(), due);
    }

    static DateTime? Day(string word, DateTime now)
    {
        if (word is "today" or "tonight") return now.Date;
        if (word is "tomorrow" or "tmrw") return now.Date.AddDays(1);

        // Weekday: the next one coming (today counts).
        var weekday = Enum.GetValues<DayOfWeek>().FirstOrDefault(d => d.ToString().ToLowerInvariant().StartsWith(word[..3]), (DayOfWeek)(-1));
        if ((int)weekday >= 0 && char.IsLetter(word[0])) return now.Date.AddDays(((int)weekday - (int)now.DayOfWeek + 7) % 7);

        // "25/11" (day/month) or "20 nov". A date already past this year means next year.
        int dayNumber, month;
        var numeric = Regex.Match(word, @"^(\d{1,2})[/-](\d{1,2})$");
        if (numeric.Success) { dayNumber = int.Parse(numeric.Groups[1].Value); month = int.Parse(numeric.Groups[2].Value); }
        else
        {
            var named = Regex.Match(word, @"^(\d{1,2})\s+([a-z]{3})");
            if (!named.Success) return null;
            dayNumber = int.Parse(named.Groups[1].Value);
            month = Array.IndexOf(new[] { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" }, named.Groups[2].Value) + 1;
        }
        if (month < 1 || month > 12 || dayNumber < 1 || dayNumber > DateTime.DaysInMonth(now.Year, month)) return null;
        var date = new DateTime(now.Year, month, dayNumber);
        return date < now.Date ? date.AddYears(1) : date;
    }

    // "5pm", "5:30 pm", "17:00"
    static TimeSpan? Time(string text)
    {
        var parts = Regex.Match(text.ToLowerInvariant(), @"(\d{1,2})(?::(\d{2}))?\s*(am|pm)?");
        int hour = int.Parse(parts.Groups[1].Value), minute = parts.Groups[2].Success ? int.Parse(parts.Groups[2].Value) : 0;
        string half = parts.Groups[3].Value;
        if (half.Length > 0 && (hour < 1 || hour > 12)) return null;
        if (half == "pm" && hour < 12) hour += 12;
        if (half == "am" && hour == 12) hour = 0;
        return hour < 24 && minute < 60 ? new TimeSpan(hour, minute, 0) : null;
    }
}
