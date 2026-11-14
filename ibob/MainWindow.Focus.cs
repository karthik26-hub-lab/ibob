using System;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ibob;

// Focus timer (Pomodoro): pick a length, start it, and a small ring in the pill shows the time left.
// When a focus session ends, ibob suggests a 5-minute break.
public partial class MainWindow
{
    static readonly Brush FocusOrange = new SolidColorBrush(Color.FromRgb(255, 159, 10));
    const int BreakMinutes = 5;

    readonly DispatcherTimer focusTick = new() { Interval = TimeSpan.FromSeconds(1) };
    TimeSpan focusLength = TimeSpan.FromMinutes(25), focusLeft = TimeSpan.FromMinutes(25);
    DateTime focusEnds;
    bool focusRunning, onBreak;

    void SetUpFocus()
    {
        focusTick.Tick += (_, _) => FocusTicked();
        ShowFocus();
    }

    // Preset chips carry their minutes in Tag ("15", "25", ...).
    void FocusPreset(object sender, RoutedEventArgs e)
    {
        onBreak = false;
        SetFocusLength(TimeSpan.FromMinutes(int.Parse((string)((Button)sender).Tag)));
    }

    void SetFocusLength(TimeSpan length)
    {
        focusRunning = false;
        focusTick.Stop();
        focusLength = focusLeft = length;
        ShowFocus();
    }

    void FocusStartPause(object sender, RoutedEventArgs e)
    {
        focusRunning = !focusRunning;
        if (focusRunning) { focusEnds = DateTime.Now + focusLeft; focusTick.Start(); }
        else { focusLeft = focusEnds - DateTime.Now; focusTick.Stop(); }
        ShowFocus();
    }

    void FocusReset(object sender, RoutedEventArgs e) => SetFocusLength(focusLength);

    void FocusTicked()
    {
        focusLeft = focusEnds - DateTime.Now;
        if (focusLeft > TimeSpan.Zero) { ShowFocus(); return; }

        // Time's up: chime, flash the pill, and line up the next step.
        Chime();
        if (onBreak)
        {
            ShowAlert(null, "Break over · back to it", FocusIndex);
            onBreak = false;
            SetFocusLength(TimeSpan.FromMinutes(25));
        }
        else
        {
            ShowAlert(null, $"Focus done · take a {BreakMinutes} min break", FocusIndex);
            onBreak = true;
            SetFocusLength(TimeSpan.FromMinutes(BreakMinutes));
        }
    }

    void ShowFocus()
    {
        var left = focusLeft < TimeSpan.Zero ? TimeSpan.Zero : focusLeft;
        FocusClock.Text = left.ToString(left.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
        FocusMode.Text = onBreak ? "Break" : "Focus";
        FocusStartText.Text = focusRunning ? "Pause" : left < focusLength ? "Resume" : "Start";

        // The ring in the tiny pill shrinks as time runs out (orange for focus, green for a break).
        FocusRing.Visibility = focusRunning ? Visibility.Visible : Visibility.Collapsed;
        FocusRing.Stroke = onBreak ? Green : FocusOrange;
        FocusRing.Data = Arc(left.TotalSeconds / focusLength.TotalSeconds, 12);
    }

    // A circular arc starting at 12 o'clock, going clockwise, covering `fraction` of the circle.
    static Geometry Arc(double fraction, double size)
    {
        double r = size / 2 - 1;
        var center = new Point(size / 2, size / 2);
        if (fraction >= 0.999) return new EllipseGeometry(center, r, r);

        double angle = Math.Max(0, fraction) * 2 * Math.PI;
        var end = new Point(center.X + r * Math.Sin(angle), center.Y - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = new Point(center.X, center.Y - r) };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        return new PathGeometry(new[] { figure });
    }
}
