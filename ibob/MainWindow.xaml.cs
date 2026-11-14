using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using Windows.Devices.Radios;
using Windows.Media.Control;
using Windows.Networking.Connectivity;
using Windows.Storage.Streams;
using Forms = System.Windows.Forms;

namespace Ibob;

public partial class MainWindow : Window
{
    static readonly Brush Green = new SolidColorBrush(Color.FromRgb(52, 199, 89));
    static readonly Brush Red = new SolidColorBrush(Color.FromRgb(255, 69, 58));
    static readonly Brush Gray = new SolidColorBrush(Color.FromRgb(44, 44, 46));
    static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(10, 132, 255));
    const string PlayGlyph = "", PauseGlyph = "";
    const int MusicIndex = 0, TodayIndex = 1, ControlsIndex = 2, NotificationsIndex = 3, ShelfIndex = 4, ClipboardIndex = 5, FocusIndex = 6;

    readonly MMDeviceEnumerator devices = new();
    MMDevice speaker, mic;
    Radio wifi, bluetooth;
    GlobalSystemMediaTransportControlsSessionManager media;

    readonly Random random = new();
    readonly DispatcherTimer mood = new();
    bool isOpen, listening, syncingBrightness;
    bool? wasCharging;
    int lastPercent, page;
    string lastSong;
    double voiceLevel, musicLevel, phase;

    public MainWindow()
    {
        InitializeComponent();
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        SourceInitialized += (_, _) => { HideFromAltTab(); WatchClipboard(); };

        var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        clock.Tick += (_, _) => Refresh();
        clock.Start();
        Refresh();

        var meter = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        meter.Tick += (_, _) => ShowSound();
        meter.Start();

        mood.Tick += (_, _) => DoSomethingCute();
        mood.Start();

        ConnectToWindows();
        LoadShelf();
        LoadHistory();
        LoadCalendars();
        SetUpFocus();
        StartNotifications();
    }

    // Gets the Wi-Fi/Bluetooth radios and the media controller (they load asynchronously).
    async void ConnectToWindows()
    {
        await Radio.RequestAccessAsync();
        var radios = await Radio.GetRadiosAsync();
        wifi = radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
        bluetooth = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
        media = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
    }

    // ---------- Every second ----------

    void Refresh()
    {
        speaker = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        try { mic = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications); } catch { mic = null; }

        var now = DateTime.Now;
        BigTime.Text = now.ToString("h:mm");
        Date.Text = now.ToString("dddd, d MMMM");

        var power = Forms.SystemInformation.PowerStatus;
        int percent = (int)Math.Round(power.BatteryLifePercent * 100);
        bool charging = power.PowerLineStatus == Forms.PowerLineStatus.Online;
        BigBattery.Text = (charging ? "⚡ " : "") + percent + "%";
        BatteryState.Text = charging ? "Charging"
            : power.BatteryLifeRemaining > 0 ? TimeSpan.FromSeconds(power.BatteryLifeRemaining).ToString(@"h\h\ mm\m") + " left"
            : "On battery";
        BigBattery.Foreground = percent <= 20 && !charging ? Red : Green;

        // Charger just plugged in, or battery just hit 100% while charging.
        if (wasCharging == false && charging) ShowStatus("⚡ " + percent + "%");
        else if (charging && percent == 100 && lastPercent < 100) ShowStatus("100%");
        wasCharging = charging;
        lastPercent = percent;

        if (!Volume.IsMouseCaptureWithin) Volume.Value = speaker.AudioEndpointVolume.MasterVolumeLevelScalar * 100;
        ShowMic();

        // The heavier checks only run while the panel is open.
        CheckToday();
        if (isOpen) { RefreshMusic(); RefreshControls(); }
    }

    // ---------- Page 1: now playing ----------

    async void RefreshMusic()
    {
        var session = media?.GetCurrentSession();
        if (session == null)
        {
            lastSong = null;
            SongTitle.Text = "Nothing playing";
            SongArtist.Text = "";
            ShowArt(null);
            Seek.Value = 0;
            Elapsed.Text = Total.Text = "0:00";
            PlayIcon.Text = PlayGlyph;
            return;
        }

        var song = await session.TryGetMediaPropertiesAsync();
        if (song.Title != lastSong)
        {
            lastSong = song.Title;
            SongTitle.Text = song.Title;
            SongArtist.Text = song.Artist;
            ShowArt(await LoadArt(song.Thumbnail));
        }

        bool playing = session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        PlayIcon.Text = playing ? PauseGlyph : PlayGlyph;

        // Apps report the position now and then, so add the time since their last report.
        var timeline = session.GetTimelineProperties();
        var position = timeline.Position + (playing ? DateTimeOffset.Now - timeline.LastUpdatedTime : TimeSpan.Zero);
        if (position > timeline.EndTime) position = timeline.EndTime;

        Seek.Maximum = Math.Max(1, timeline.EndTime.TotalSeconds);
        if (!Seek.IsMouseCaptureWithin) Seek.Value = position.TotalSeconds;
        Total.Text = Clock(timeline.EndTime);
    }

    static async Task<ImageSource> LoadArt(IRandomAccessStreamReference thumbnail)
    {
        if (thumbnail == null) return null;
        try
        {
            using var stream = (await thumbnail.OpenReadAsync()).AsStreamForRead();
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            return image;
        }
        catch { return null; }
    }

    void ShowArt(ImageSource image)
    {
        Art.Background = image == null ? Gray : new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        ArtIcon.Visibility = image == null ? Visibility.Visible : Visibility.Collapsed;
    }

    static string Clock(TimeSpan time) => time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");

    void SeekMoved(object sender, RoutedPropertyChangedEventArgs<double> e) => Elapsed.Text = Clock(TimeSpan.FromSeconds(e.NewValue));

    async void SeekReleased(object sender, MouseButtonEventArgs e)
    {
        var session = media?.GetCurrentSession();
        if (session != null) await session.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(Seek.Value).Ticks);
    }

    async void PlayPause(object sender, RoutedEventArgs e) { await (media?.GetCurrentSession()?.TryTogglePlayPauseAsync()); RefreshMusic(); }
    async void NextSong(object sender, RoutedEventArgs e) { await (media?.GetCurrentSession()?.TrySkipNextAsync()); RefreshMusic(); }
    async void PreviousSong(object sender, RoutedEventArgs e) { await (media?.GetCurrentSession()?.TrySkipPreviousAsync()); RefreshMusic(); }

    // ---------- Page 2: controls ----------

    void RefreshControls()
    {
        bool wifiOn = wifi?.State == RadioState.On;
        var network = NetworkInformation.GetInternetConnectionProfile();
        WifiText.Text = !wifiOn ? "Off" : network?.IsWlanConnectionProfile == true ? network.ProfileName : "Wi-Fi";
        WifiButton.Background = wifiOn ? Blue : Gray;

        bool bluetoothOn = bluetooth?.State == RadioState.On;
        BluetoothText.Text = bluetooth == null ? "None" : bluetoothOn ? "On" : "Off";
        BluetoothButton.Background = bluetoothOn ? Blue : Gray;

        int brightness = GetBrightness();
        BrightnessRow.Visibility = brightness < 0 ? Visibility.Collapsed : Visibility.Visible;
        if (brightness >= 0 && !Brightness.IsMouseCaptureWithin)
        {
            syncingBrightness = true;
            Brightness.Value = brightness;
            syncingBrightness = false;
        }
    }

    void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        speaker.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(e.NewValue / 100);
        VolumeText.Text = (int)e.NewValue + "%";
    }

    void BrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        BrightnessText.Text = (int)e.NewValue + "%";
        if (!syncingBrightness) SetBrightness((int)e.NewValue);
    }

    // Laptop screen brightness through WMI. Returns -1 if the screen doesn't support it (e.g. external monitor).
    static int GetBrightness()
    {
        try
        {
            using var search = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject screen in search.Get()) return (byte)screen["CurrentBrightness"];
        }
        catch { }
        return -1;
    }

    static void SetBrightness(int percent)
    {
        using var search = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
        foreach (ManagementObject screen in search.Get()) screen.InvokeMethod("WmiSetBrightness", new object[] { 1, (byte)percent });
    }

    async void ToggleWifi(object sender, RoutedEventArgs e) => await Toggle(wifi);
    async void ToggleBluetooth(object sender, RoutedEventArgs e) => await Toggle(bluetooth);

    async Task Toggle(Radio radio)
    {
        if (radio == null) return;
        await radio.SetStateAsync(radio.State == RadioState.On ? RadioState.Off : RadioState.On);
        RefreshControls();
    }

    void ToggleMic(object sender, RoutedEventArgs e)
    {
        if (mic != null) mic.AudioEndpointVolume.Mute = !mic.AudioEndpointVolume.Mute;
        ShowMic();
    }

    void ShowMic()
    {
        if (mic == null) { MicIcon.Text = ""; MicText.Text = "No mic"; MicButton.Background = Gray; return; }

        bool muted = mic.AudioEndpointVolume.Mute;
        MicIcon.Text = muted ? "" : "";
        MicText.Text = muted ? "Mic off" : "Mic on";
        MicButton.Background = muted ? Red : Gray;
        MeetingMuteText.Text = muted ? "Unmute" : "Mute mic";
        MeetingMute.Background = muted ? Red : Gray;
    }

    // ---------- The little face ----------

    // Runs ~30 times a second: voice line while you talk, nodding eyes while music plays.
    void ShowSound()
    {
        float voice = mic?.AudioMeterInformation.MasterPeakValue ?? 0;
        float music = speaker.AudioMeterInformation.MasterPeakValue;

        // Smooth the levels so the line and the nod glide instead of jumping.
        voiceLevel += ((voice > 0.02 ? Math.Sqrt(voice) : 0) - voiceLevel) * 0.35;
        musicLevel += ((music > 0.02 ? Math.Sqrt(music) : 0) - musicLevel) * 0.5;

        bool talking = voiceLevel > 0.03;
        listening = !talking && music > 0.02;
        Alert.Visibility = alerting ? Visibility.Visible : Visibility.Collapsed;
        Wave.Visibility = talking && !alerting ? Visibility.Visible : Visibility.Collapsed;
        Eyes.Visibility = talking || alerting ? Visibility.Collapsed : Visibility.Visible;

        // Happy squinted eyes that bob with the beat.
        Squint.ScaleY = listening ? 0.4 : 1;
        Bob.Y = listening ? -musicLevel * 4 : 0;

        if (talking) DrawWave();
    }

    // A thin flowing line whose height follows how loud you're speaking, tapered at both ends.
    void DrawWave()
    {
        phase += 0.4;
        var points = new PointCollection();
        for (double x = 0; x <= 64; x += 2)
        {
            double taper = Math.Sin(Math.PI * x / 64);
            double shape = 0.7 * Math.Sin(x * 0.25 + phase) + 0.3 * Math.Sin(x * 0.55 - phase * 1.3);
            points.Add(new Point(x, 11 + shape * voiceLevel * 9 * taper));
        }
        Wave.Points = points;
    }

    // Shows a short message (like charging) in place of the eyes for two seconds.
    void ShowStatus(string text)
    {
        Status.Text = text;
        Status.Foreground = Green;
        Eyes.BeginAnimation(OpacityProperty, Hold(1, 0, 2));
        Status.BeginAnimation(OpacityProperty, Hold(0, 1, 2));
        StatusScale.BeginAnimation(ScaleTransform.ScaleXProperty, Hold(0, 1, 2));
        StatusScale.BeginAnimation(ScaleTransform.ScaleYProperty, Hold(0, 1, 2));
    }

    // Every few seconds the eyes blink or glance sideways.
    void DoSomethingCute()
    {
        mood.Interval = TimeSpan.FromSeconds(2 + random.NextDouble() * 4);
        if (isOpen || listening || alerting || Wave.IsVisible) return;

        if (random.Next(3) < 2)
            EyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.1, TimeSpan.FromMilliseconds(90)) { AutoReverse = true });
        else
            EyeMove.BeginAnimation(TranslateTransform.XProperty, Hold(0, random.Next(2) == 0 ? -3 : 3, 1));
    }

    // Bounces to a value, holds it for a while, then eases back to rest.
    static DoubleAnimationUsingKeyFrames Hold(double rest, double to, double seconds)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.25)), new BackEase { EasingMode = EasingMode.EaseOut }));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.25 + seconds))));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(rest, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5 + seconds))));
        return animation;
    }

    // ---------- Opening, closing and paging ----------

    // Opens on: the notifications page if one just popped up, else music if something is playing, else controls.
    void Expand(object sender, MouseEventArgs e)
    {
        if (isOpen) return;
        Open(alerting ? alertPage
            : MeetingSoon ? TodayIndex
            : media?.GetCurrentSession() != null ? MusicIndex
            : ControlsIndex);
    }

    void Open(int index)
    {
        alerting = false;
        alertTimer.Stop();
        Animate(420, 200, open: true);
        RefreshMusic();
        RefreshControls();
        _ = ShowNotifications();
        ShowPage(index);
    }

    // The ring sits on top of the pill, so moving onto it isn't "leaving" the island.
    void Collapse(object sender, MouseEventArgs e)
    {
        if (sender != null && radialOpen) return;
        Animate(84, 22, open: false);
    }

    FrameworkElement[] Pages => new FrameworkElement[] { MusicPage, TodayPage, ControlsPage, NotificationsPage, ShelfPage, ClipboardPage, FocusPage };

    // Wheel down → next page, wheel up → previous page.
    void Scrolled(object sender, MouseWheelEventArgs e)
    {
        int next = Math.Clamp(page + (e.Delta < 0 ? 1 : -1), 0, Pages.Length - 1);
        if (isOpen && !boardOpen && next != page) ShowPage(next);
    }

    // Slides the chosen page in from the direction you scrolled.
    void ShowPage(int index)
    {
        double from = index > page ? 30 : -30;
        page = index;
        var pages = Pages;
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i].Visibility = i == page ? Visibility.Visible : Visibility.Collapsed;
            Dots.Children[i].Opacity = i == page ? 1 : 0.3;
        }

        var shown = pages[page];
        var slide = new TranslateTransform();
        shown.RenderTransform = slide;
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        shown.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
    }

    // Springy grow/shrink of the pill, swapping the mini face for the full panel.
    void Animate(double width, double height, bool open)
    {
        isOpen = open;
        if (!open) { HideBoard(); CloseRadial(); EndMerge(); }
        SizePill(width, height, open ? 28 : 11, open ? 380 : 220);

        Mini.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        Big.Visibility = Dots.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)) { BeginTime = TimeSpan.FromMilliseconds(80) };
        (open ? Big : (UIElement)Mini).BeginAnimation(OpacityProperty, fade);
    }

    void SizePill(double width, double height, double radius, int milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(milliseconds);
        var ease = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
        Pill.BeginAnimation(WidthProperty, new DoubleAnimation(width, time) { EasingFunction = ease });
        Pill.BeginAnimation(HeightProperty, new DoubleAnimation(height, time) { EasingFunction = ease });
        Pill.CornerRadius = new CornerRadius(radius);
    }

    void Quit(object sender, RoutedEventArgs e) => Close();

    // Marks the window as a tool window so it doesn't show up in Alt+Tab.
    void HideFromAltTab()
    {
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x80);
    }

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int index, int value);
}
