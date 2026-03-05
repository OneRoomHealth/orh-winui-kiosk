using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace KioskApp;

/// <summary>
/// MainWindow partial class - Tab bar management for techtablet machine type.
/// </summary>
public sealed partial class MainWindow
{
    // ── Inner model ────────────────────────────────────────────────────────
    private sealed class TabInfo
    {
        public string Url    { get; set; } = "";
        public string Title  { get; set; } = "New Tab";
        public WebView2? WebView { get; set; }   // null = uses KioskWebView
        public Button    TabButton  { get; set; } = null!;
        public TextBlock TitleLabel { get; set; } = null!;
    }

    // ── State ──────────────────────────────────────────────────────────────
    private readonly List<TabInfo> _tabs = new();
    private int  _activeTabIndex = -1;

    // ── Initialization ────────────────────────────────────────────────────
    private void InitializeTabMode()
    {
        if (!_config.Kiosk.MachineType.Equals("techtablet", StringComparison.OrdinalIgnoreCase))
            return;

        _isTabMode = true;
        TabBarContainer.Visibility = Visibility.Visible;

        // First tab is backed by the already-initialized KioskWebView
        var firstTab = new TabInfo { Url = _config.Kiosk.DefaultUrl, Title = "Home" };
        firstTab.TabButton = BuildTabButton(firstTab, isFirst: true);
        TabBarPanel.Children.Add(firstTab.TabButton);
        _tabs.Add(firstTab);
        _activeTabIndex = 0;

        // Keep first tab title in sync with page title
        if (KioskWebView.CoreWebView2 != null)
            KioskWebView.CoreWebView2.DocumentTitleChanged += (_, _) =>
                UpdateTabTitle(firstTab, KioskWebView.CoreWebView2.DocumentTitle);

        MarkTabActive(firstTab.TabButton);
        Logger.Log("[TABS] Tab mode initialized");
    }

    // ── Add / Close / Switch ──────────────────────────────────────────────
    private async Task AddNewTabAsync(string? url = null)
    {
        url ??= _config.Kiosk.DefaultUrl;

        var webView = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
            DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0),
            Visibility = Visibility.Collapsed
        };

        // Insert before StatusOverlay (last child of WebViewContainer)
        WebViewContainer.Children.Insert(WebViewContainer.Children.Count - 1, webView);

        var env = await CoreWebView2Environment.CreateAsync();
        await webView.EnsureCoreWebView2Async(env);

        // Apply touch-optimised settings
        ApplyTabletWebViewSettings(webView.CoreWebView2.Settings, _isDebugMode);

        // Auto-allow permissions (mirrors KioskWebView behaviour)
        webView.CoreWebView2.PermissionRequested += (_, args) =>
        {
            args.State = CoreWebView2PermissionState.Allow;
            args.SavesInProfile = true;
        };

        var newTab = new TabInfo { Url = url, WebView = webView };
        newTab.TabButton = BuildTabButton(newTab, isFirst: false);
        TabBarPanel.Children.Add(newTab.TabButton);
        _tabs.Add(newTab);

        // Keep title in sync
        webView.CoreWebView2.DocumentTitleChanged += (_, _) =>
            UpdateTabTitle(newTab, webView.CoreWebView2.DocumentTitle);

        // Track URL changes so the debug URL bar stays in sync
        webView.CoreWebView2.NavigationCompleted += (_, _) =>
        {
            var navigatedUrl = webView.Source?.ToString() ?? "";
            newTab.Url = navigatedUrl;
            if (_isDebugMode && _activeTabIndex == _tabs.IndexOf(newTab) && UrlTextBox != null)
                DispatcherQueue.TryEnqueue(() => { if (UrlTextBox != null) UrlTextBox.Text = navigatedUrl; });
        };

        // New-window requests from this tab → open as another tab
        webView.CoreWebView2.NewWindowRequested += (_, args) =>
        {
            var newUrl = args.Uri;
            args.Handled = true;
            DispatcherQueue.TryEnqueue(async () => await AddNewTabAsync(newUrl));
        };

        webView.Source = new Uri(url);
        SwitchToTab(newTab);
        Logger.Log($"[TABS] New tab opened: {url}");
    }

    private void SwitchToTab(TabInfo target)
    {
        var targetIndex = _tabs.IndexOf(target);
        if (targetIndex < 0) return;

        for (int i = 0; i < _tabs.Count; i++)
        {
            var wv = i == 0 ? (UIElement)KioskWebView : (UIElement?)_tabs[i].WebView;
            if (wv != null) wv.Visibility = i == targetIndex ? Visibility.Visible : Visibility.Collapsed;
            UpdateTabButtonStyle(_tabs[i].TabButton, i == targetIndex);
        }
        _activeTabIndex = targetIndex;

        // Keep debug URL bar in sync with the newly-active tab
        if (_isDebugMode && UrlTextBox != null)
            UrlTextBox.Text = GetActiveTabUrl();
    }

    /// <summary>Returns the URL of the currently-active tab (or _currentUrl for non-tab mode).</summary>
    internal string GetActiveTabUrl()
    {
        if (!_isTabMode || _activeTabIndex < 0 || _activeTabIndex >= _tabs.Count)
            return _currentUrl;

        return _activeTabIndex == 0
            ? (KioskWebView.Source?.ToString() ?? _currentUrl)
            : (_tabs[_activeTabIndex].WebView?.Source?.ToString() ?? _tabs[_activeTabIndex].Url);
    }

    private void CloseTab(TabInfo tab)
    {
        var index = _tabs.IndexOf(tab);
        if (index <= 0) return;  // first tab cannot be closed

        SwitchToTab(_tabs[index > 0 ? index - 1 : 0]);

        TabBarPanel.Children.Remove(tab.TabButton);
        if (tab.WebView != null)
        {
            WebViewContainer.Children.Remove(tab.WebView);
            tab.WebView.Close();
        }
        _tabs.RemoveAt(index);
        Logger.Log($"[TABS] Tab closed: {tab.Title}");
    }

    // ── Click handlers ────────────────────────────────────────────────────
    private void NewTabButton_Click(object sender, RoutedEventArgs e)
        => _ = AddNewTabAsync();

    // ── Helpers ───────────────────────────────────────────────────────────
    private Button BuildTabButton(TabInfo tab, bool isFirst)
    {
        var titleLabel = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
            MaxWidth = 140,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 204, 204, 204)),
            Text = tab.Title
        };
        tab.TitleLabel = titleLabel;

        var inner = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        inner.Children.Add(titleLabel);

        if (!isFirst)
        {
            var closeBtn = new Button
            {
                Content = "×",
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            };
            closeBtn.Click += (_, _) => CloseTab(tab);
            inner.Children.Add(closeBtn);
        }

        var btn = new Button
        {
            Content = inner,
            Height = 40,
            MinWidth = 100,
            MaxWidth = 200,
            Padding = new Thickness(12, 0, 8, 0),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 45, 45, 45)),
        };
        btn.Click += (_, _) => SwitchToTab(tab);
        return btn;
    }

    private void UpdateTabTitle(TabInfo tab, string? title)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var label = string.IsNullOrWhiteSpace(title) ? tab.Url : title;
            tab.Title = label;
            tab.TitleLabel.Text = label;
        });
    }

    private static void MarkTabActive(Button btn) => UpdateTabButtonStyle(btn, true);
    private static void UpdateTabButtonStyle(Button btn, bool active)
    {
        // Active tab: lighter gray — Inactive tab: darker gray for clear contrast
        btn.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            active
                ? Windows.UI.Color.FromArgb(255, 63, 63, 63)   // #3F3F3F — active
                : Windows.UI.Color.FromArgb(255, 37, 37, 38));  // #252526 — inactive (matches bar)
        btn.Opacity = 1.0;
    }
}
