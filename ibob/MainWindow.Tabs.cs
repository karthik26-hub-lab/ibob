using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Ibob;

// The tab bar at the top of the open island: one icon per page, so any page is a single click away.
public partial class MainWindow
{
    const double OpenHeight = 230;   // open island: 14 top + 28 tab bar + 10 gap + 164 page + 14 bottom
    static readonly Brush TabSelected = new SolidColorBrush(Color.FromRgb(44, 44, 46));

    // Each tab's name (shown on hover and in Settings) and icon, in tab-bar order.
    static readonly (string Name, string Glyph)[] TabInfo =
    {
        ("Home", ""), ("Plan", ""), ("Notes", ""),
        ("Shelf", ""), ("Clipboard", ""), ("Focus", ""),
    };
    public static string[] AllPageNames => TabInfo.Select(tab => tab.Name).ToArray();
    static int TabCount => TabInfo.Length;

    // Same order as the tabs; Notifications comes last because it opens from the bell, not a tab.
    FrameworkElement[] Pages => new FrameworkElement[] { HomePage, PlanPage, NotesPage, ShelfPage, ClipboardPage, FocusPage, NotificationsPage };

    readonly List<Button> tabButtons = new();

    void BuildTabs()
    {
        foreach (var (name, glyph) in TabInfo)
        {
            int index = tabButtons.Count;
            var button = new Button
            {
                Style = (Style)FindResource("Chip"), Width = 34, Height = 28, Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 0), ToolTip = name,
                Content = new TextBlock { Text = glyph, Style = (Style)FindResource("Icon"), FontSize = 14 },
            };
            button.Click += (_, _) => ShowPage(index);
            tabButtons.Add(button);
            Tabs.Children.Add(button);
        }
    }

    // Current tab: lighter background and a white icon. Tabs hidden in Settings disappear from the bar.
    void HighlightTab()
    {
        for (int i = 0; i < tabButtons.Count; i++)
        {
            tabButtons[i].Background = i == page ? TabSelected : Brushes.Transparent;
            ((TextBlock)tabButtons[i].Content).Foreground = i == page ? Brushes.White : Gray3;
            tabButtons[i].Visibility = PageShown(i) ? Visibility.Visible : Visibility.Collapsed;
        }
        BellButton.Background = page == NotificationsIndex ? TabSelected : Brushes.Transparent;
        BellIcon.Foreground = page == NotificationsIndex ? Brushes.White : Gray3;
    }

    void BellClicked(object sender, RoutedEventArgs e) => ShowPage(NotificationsIndex);

    // Small red count on the bell (hidden when there are no notifications).
    void ShowBadge(int count)
    {
        BellBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BellCount.Text = count > 9 ? "9+" : count.ToString();
    }

    void ShowPage(int index)
    {
        index = ShownPage(index);
        if (index == HomeIndex) ShowHomeScreen(quick: false);   // Home always opens on the music screen
        double from = index > page ? 30 : -30;
        page = index;
        var pages = Pages;
        for (int i = 0; i < pages.Length; i++)
            pages[i].Visibility = i == page ? Visibility.Visible : Visibility.Collapsed;
        HighlightTab();

        // Slide the page in from the direction you moved.
        var shown = pages[page];
        var slide = new TranslateTransform();
        shown.RenderTransform = slide;
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        shown.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
    }

    // Mouse wheel: a list under the mouse (tasks, notes, ...) scrolls first. Once it's at its end,
    // or over anything that doesn't scroll, the wheel moves to the next / previous tab, so it never gets stuck.
    // On Home, the wheel first moves between its two screens (music ↔ quick settings), then on to the next tab.
    void Scrolled(object sender, MouseWheelEventArgs e)
    {
        if (!isOpen || boardOpen || radialOpen) return;
        if (CanStillScroll(e.OriginalSource as DependencyObject, e.Delta)) return;
        e.Handled = true;

        bool down = e.Delta < 0;
        if (page == HomeIndex && down != homeQuick) { ShowHomeScreen(quick: down); return; }

        int next = NextShownPage(page, down ? 1 : -1);
        if (next == page) return;
        ShowPage(next);
        if (next == HomeIndex && !down) ShowHomeScreen(quick: true);   // coming back up lands on quick settings
    }

    bool homeQuick;   // false = music screen, true = quick settings screen

    void ShowHomeScreen(bool quick)
    {
        homeQuick = quick;
        MusicView.Visibility = quick ? Visibility.Collapsed : Visibility.Visible;
        QuickView.Visibility = quick ? Visibility.Visible : Visibility.Collapsed;
        HomeDot1.Opacity = quick ? 0.3 : 1;
        HomeDot2.Opacity = quick ? 1 : 0.3;

        FrameworkElement shown = quick ? QuickView : MusicView;
        var slide = new TranslateTransform();
        shown.RenderTransform = slide;
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(quick ? 24 : -24, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        shown.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
    }

    static bool CanStillScroll(DependencyObject element, int delta)
    {
        for (; element != null; element = Parent(element))
            if (element is ScrollViewer list && list.ScrollableHeight > 0)
                return delta > 0 ? list.VerticalOffset > 0 : list.VerticalOffset < list.ScrollableHeight;
        return false;
    }

    // Text pieces (like a Run inside a TextBlock) aren't visuals, so walk up through the logical tree for those.
    static DependencyObject Parent(DependencyObject element) =>
        element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
}
