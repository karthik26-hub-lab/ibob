using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Ibob;

// One row on the notifications page.
public record Note(ImageSource Icon, string Title, string Body, string Time);

// Reads Windows notifications, lists the latest three, and flashes new ones in the pill.
public partial class MainWindow
{
    UserNotificationListener listener;
    HashSet<uint> seenNotifications;   // stays null until the first check, so old notifications don't pop up at startup
    readonly DispatcherTimer alertTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    bool alerting;
    int alertPage = NotificationsIndex;   // which page hovering opens while an alert is showing

    async void StartNotifications()
    {
        listener = UserNotificationListener.Current;
        if (await listener.RequestAccessAsync() != UserNotificationListenerAccessStatus.Allowed)
        {
            listener = null;
            NoNotifications.Text = "Turn on notification access for apps in\nSettings → Privacy & security → Notifications";
            return;
        }

        alertTimer.Tick += (_, _) => EndAlert();
        var poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        poll.Tick += (_, _) => CheckNotifications();
        poll.Start();
        CheckNotifications();
    }

    // Every 2 seconds: if the set of notifications changed, refresh the list and flash the newest one.
    async void CheckNotifications()
    {
        IReadOnlyList<UserNotification> all;
        try { all = await listener.GetNotificationsAsync(NotificationKinds.Toast); } catch { return; }

        var ids = all.Select(n => n.Id).ToHashSet();
        if (seenNotifications != null && ids.SetEquals(seenNotifications)) return;

        var fresh = seenNotifications == null ? null
            : all.Where(n => !seenNotifications.Contains(n.Id)).OrderByDescending(n => n.CreationTime).FirstOrDefault();
        seenNotifications = ids;

        await ShowNotifications();
        if (fresh != null)
        {
            var note = await ToNote(fresh);
            ShowAlert(note.Icon, note.Title, NotificationsIndex);
        }
    }

    async Task ShowNotifications()
    {
        if (listener == null) return;
        IReadOnlyList<UserNotification> all;
        try { all = await listener.GetNotificationsAsync(NotificationKinds.Toast); } catch { return; }

        var notes = new List<Note>();
        foreach (var n in all.OrderByDescending(n => n.CreationTime).Take(3)) notes.Add(await ToNote(n));
        NotificationList.ItemsSource = notes;
        NoNotifications.Visibility = notes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // First text line is usually the sender/title, the rest is the message.
    static async Task<Note> ToNote(UserNotification n)
    {
        string app = n.AppInfo.DisplayInfo.DisplayName;
        var texts = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric)?
            .GetTextElements().Select(t => t.Text).ToList() ?? new List<string>();

        ImageSource icon = null;
        try { icon = await LoadArt(n.AppInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(32, 32))); } catch { }

        return new Note(icon, texts.FirstOrDefault() ?? app, string.Join(" ", texts.Skip(1)), Ago(n.CreationTime));
    }

    static string Ago(DateTimeOffset time)
    {
        var span = DateTimeOffset.Now - time;
        return span.TotalMinutes < 1 ? "now"
            : span.TotalHours < 1 ? (int)span.TotalMinutes + "m"
            : span.TotalDays < 1 ? (int)span.TotalHours + "h"
            : (int)span.TotalDays + "d";
    }

    // Widens the pill for a few seconds with an icon and a line of text (new notification, file saved to shelf).
    void ShowAlert(ImageSource icon, string text, int page)
    {
        if (isOpen) return;
        AlertIcon.Visibility = icon == null ? Visibility.Collapsed : Visibility.Visible;   // timer alerts have no icon
        AlertIcon.Background = icon == null ? null : new ImageBrush(icon) { Stretch = Stretch.Uniform };
        AlertText.Text = text;
        alertPage = page;
        alerting = true;
        SizePill(240, 30, 15, 380);
        alertTimer.Stop();
        alertTimer.Start();
    }

    void EndAlert()
    {
        alertTimer.Stop();
        if (!alerting) return;
        alerting = false;
        if (!isOpen) SizePill(84, 22, 11, 220);
    }

    // Windows doesn't let this app clear everything at once, so remove them one by one.
    async void ClearNotifications(object sender, RoutedEventArgs e)
    {
        if (listener == null) return;
        foreach (var n in await listener.GetNotificationsAsync(NotificationKinds.Toast))
            try { listener.RemoveNotification(n.Id); } catch { }
        await ShowNotifications();
    }
}
