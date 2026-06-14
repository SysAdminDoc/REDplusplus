using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NotBob.Config;
using RED.Config;
using RED.Match;
using TXT = RED.RedGetText;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfImage = System.Windows.Controls.Image;
using WpfMessageBox = System.Windows.MessageBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using Forms = System.Windows.Forms;

namespace RED.UI.Wpf
{
    internal sealed class ModernMainWindow : Window
    {
        private readonly ObservableCollection<ResultRow> results = new ObservableCollection<ResultRow>();
        private readonly Dictionary<string, ResultRow> rowsByPath = new Dictionary<string, ResultRow>(StringComparer.OrdinalIgnoreCase);
        private readonly string initialPath;
        private readonly bool autoSearch;

        private RedConfiguration config;
        private RuntimeData runData;
        private REDCore core;
        private Stopwatch runtimeWatch = new Stopwatch();
        private DispatcherTimer forwardSignalTimer;

        private Grid rootGrid;
        private Grid contentHost;
        private Border resultSurface;
        private Grid emptyState;
        private System.Windows.Shapes.Path emptyIcon;
        private TextBlock emptyTitle;
        private TextBlock emptySubtitle;
        private StackPanel emptyTrust;
        private bool hasScanned;
        private ListView resultsList;
        private WpfTextBox pathBox;
        private WpfButton scanButton;
        private WpfButton deleteButton;
        private WpfButton cancelButton;
        private WpfButton extrasButton;
        private WpfButton exitButton;
        private TextBlock readyText;
        private TextBlock itemCountText;
        private TextBlock detailStatusText;
        private TextBlock progressText;
        private ProgressBar progressBar;
        private StackPanel tabPanel;
        private string selectedTab = "Search";
        private WpfCheckBox ignoreEmptyFiles;
        private WpfCheckBox deleteEmptyFiles;
        private WpfCheckBox ignoreSystem;
        private WpfCheckBox ignoreHidden;
        private WpfCheckBox hideDeletionErrors;
        private WpfCheckBox hideScanErrors;
        private WpfCheckBox hideIgnored;
        private WpfCheckBox protectRoot;
        private WpfCheckBox fastRendering;
        private WpfCheckBox clipboardDetection;
        private WpfCheckBox respectGitIgnore;
        private WpfCheckBox useMft;
        private WpfComboBox deleteMode;
        private bool appliedPhysicalStartupBounds;

        private const double DefaultWindowWidth = 1180d;
        private const double DefaultWindowHeight = 820d;
        private const double PreferredMinWidth = 1020d;
        private const double PreferredMinHeight = 660d;

        private static readonly Brush Bg = BrushFrom("#0b1420");
        private static readonly Brush Bg2 = BrushFrom("#101b2a");
        private static readonly Brush Panel = BrushFrom("#152235");
        private static readonly Brush Panel2 = BrushFrom("#1b2940");
        private static readonly Brush Border = BrushFrom("#3b4a61");
        private static readonly Brush BorderStrong = BrushFrom("#53647d");
        private static readonly Brush Text = BrushFrom("#e8eefb");
        private static readonly Brush Muted = BrushFrom("#a7b3c9");
        private static readonly Brush Muted2 = BrushFrom("#74829b");
        private static readonly Brush Blue = BrushFrom("#2f6df2");
        private static readonly Brush BlueLight = BrushFrom("#7aa8ff");
        private static readonly Brush Red = BrushFrom("#dc3548");
        // A brighter red than the legend swatch, for status text that must stay legible
        // on the dark result surface (the #dc3548 swatch dips below AA at body size).
        private static readonly Brush RedText = BrushFrom("#ff7a86");
        private static readonly Brush Green = BrushFrom("#67d16f");
        private static readonly Brush Amber = BrushFrom("#ffca55");
        private static readonly Brush Pink = BrushFrom("#f17aa5");

        // Keyboard-focus ring for the non-button controls (text box, combo, lists),
        // which otherwise show only WPF's near-invisible dotted default on this dark
        // surface. Matches the BlueLight ring the buttons already use (WCAG 2.4.7).
        private static readonly Style FocusVisual = CreateFocusVisual();
        private static readonly Geometry IconSearch = Glyph("M17,8 A9,9 0 1 1 17,26 A9,9 0 1 1 17,8 M24,24 L32,32");
        private static readonly Geometry IconSettings = Glyph("M18,5 L20.6,9.7 L26,10.8 L22.4,15 L24.1,20.3 L18.8,19 L15.2,23.2 L12.6,18.5 L7.2,17.4 L10.8,13.2 L9.1,7.9 L14.4,9.2 Z M18,13 A5,5 0 1 1 17.9,13");
        private static readonly Geometry IconFilter = Glyph("M7,7 L31,7 L22,18 L22,29 L16,32 L16,18 Z");
        private static readonly Geometry IconInfo = Glyph("M18,5 A13,13 0 1 1 17.9,5 M18,16 L18,27 M18,11 L18,11");
        private static readonly Geometry IconTrash = Glyph("M10,11 L26,11 M13,11 L13,29 L23,29 L23,11 M15,7 L21,7 M16,15 L16,25 M20,15 L20,25");
        private static readonly Geometry IconCancel = Glyph("M10,10 L28,28 M28,10 L10,28");
        private static readonly Geometry IconFolder = Glyph("M7,14 L15,14 L18,17 L33,17 L33,30 L7,30 Z");
        private static readonly Geometry IconHome = Glyph("M7,19 L20,8 L33,19 M12,18 L12,31 L28,31 L28,18 M17,31 L17,24 L23,24 L23,31");
        private static readonly Geometry IconHidden = Glyph("M8,12 L32,12 L32,29 L8,29 Z");
        private static readonly Geometry IconLock = Glyph("M12,17 L28,17 L28,31 L12,31 Z M16,17 L16,13 A4,4 0 0 1 24,13 L24,17 M20,22 L20,26");
        private static readonly Geometry IconNeverEmpty = Glyph("M7,14 L15,14 L18,17 L33,17 L33,30 L7,30 Z M16,23 L24,23");
        private static readonly Geometry IconWarning = Glyph("M20,7 L33,31 L7,31 Z M20,15 L20,23 M20,27 L20,27");
        private static readonly Geometry IconShield = Glyph("M20,6 L31,11 L28,27 L20,33 L12,27 L9,11 Z M15,20 L19,24 L26,15");
        private static readonly Geometry IconCheck = Glyph("M8,22 L16,30 L32,10");
        private static readonly Geometry IconMinimize = Glyph("M10,20 L28,20");
        private static readonly Geometry IconMaximize = Glyph("M11,11 L27,11 L27,27 L11,27 Z");
        private static readonly Geometry IconClose = Glyph("M11,11 L27,27 M27,11 L11,27");

        public ModernMainWindow(string startPath, bool shouldAutoSearch)
        {
            initialPath = startPath;
            autoSearch = shouldAutoSearch;
            Title = "RED++ - Remove Empty Directories+";
            ApplyInitialWindowBounds();
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Background = Bg;
            Icon = ToImageSource(Properties.Resources.iconProject);

            LoadConfig();
            BuildUi();
            ApplyConfigToUi();
            UpdateUiState(false);
            StartForwardWatcher();

            SourceInitialized += (s, e) => ApplyPhysicalStartupBounds();
            Loaded += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(initialPath))
                {
                    pathBox.Text = EnsureTrailingSlash(initialPath);
                    if (autoSearch)
                    {
                        StartScan();
                    }
                }
            };
            Closed += (s, e) =>
            {
                if (forwardSignalTimer != null)
                {
                    forwardSignalTimer.Stop();
                    forwardSignalTimer = null;
                }
                try
                {
                    UpdateConfigFromUi();
                    ConfigAssist.ConfigSaveWithPrompt(config, false);
                }
                catch
                {
                    // Shutdown should not be blocked by settings persistence.
                }
                if (runData != null)
                {
                    runData.Dispose();
                    runData = null;
                }
            };
        }

        private void LoadConfig()
        {
            ConfigAssist.ConfigLoad(ref config, "RemoveEmptyDirectories");
        }

        private void BuildUi()
        {
            rootGrid = new Grid { Background = Bg };
            rootGrid.Resources.Add(typeof(WpfButton), CreateButtonStyle());
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(76) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            Content = rootGrid;

            BuildTitleBar();
            BuildTabs();
            BuildContent();
            BuildCommandBar();
            BuildStatusBar();
        }

        private static Style CreateButtonStyle()
        {
            var style = new Style(typeof(WpfButton));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Panel2));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Border));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateButtonTemplate()));

            // Disabled is the only state safe to express as a Style trigger: every
            // button below sets Background/BorderBrush as *local* values, which
            // outrank Style-trigger setters. Hover/press/focus must therefore live
            // in the ControlTemplate (see CreateButtonTemplate) so they apply to
            // colored and transparent buttons alike — including keyboard focus,
            // which was previously invisible on every button (WCAG 2.4.7).
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.42));
            style.Triggers.Add(disabled);

            return style;
        }

        private static ControlTemplate CreateButtonTemplate()
        {
            var template = new ControlTemplate(typeof(WpfButton));
            var border = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            border.Name = "ButtonChrome";
            border.SetValue(System.Windows.Controls.Border.SnapsToDevicePixelsProperty, true);
            border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetBinding(System.Windows.Controls.Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(System.Windows.Controls.Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(System.Windows.Controls.Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });

            var layers = new FrameworkElementFactory(typeof(Grid));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.SnapsToDevicePixelsProperty, true);
            presenter.SetBinding(ContentPresenter.ContentProperty, new Binding("Content") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("ContentTemplate") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetBinding(ContentPresenter.HorizontalAlignmentProperty, new Binding("HorizontalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetBinding(ContentPresenter.VerticalAlignmentProperty, new Binding("VerticalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
            layers.AppendChild(presenter);

            // Tint overlay (above content, non-interactive) gives every button
            // hover/press feedback regardless of its base color.
            var overlay = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            overlay.Name = "HoverOverlay";
            overlay.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(4));
            overlay.SetValue(System.Windows.Controls.Border.BackgroundProperty, Brushes.Transparent);
            overlay.SetValue(UIElement.IsHitTestVisibleProperty, false);
            layers.AppendChild(overlay);

            // Keyboard focus ring — an inner border that is transparent until the
            // button takes keyboard focus, so it is visible on any background.
            var focusRing = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            focusRing.Name = "FocusRing";
            focusRing.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(4));
            focusRing.SetValue(System.Windows.Controls.Border.BorderBrushProperty, Brushes.Transparent);
            focusRing.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(2));
            focusRing.SetValue(UIElement.IsHitTestVisibleProperty, false);
            focusRing.SetValue(FrameworkElement.MarginProperty, new Thickness(1));
            layers.AppendChild(focusRing);

            border.AppendChild(layers);
            template.VisualTree = border;

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(System.Windows.Controls.Border.BackgroundProperty, BrushFrom("#1AFFFFFF"), "HoverOverlay"));
            template.Triggers.Add(hover);

            var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(System.Windows.Controls.Border.BackgroundProperty, BrushFrom("#33000000"), "HoverOverlay"));
            template.Triggers.Add(pressed);

            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(System.Windows.Controls.Border.BorderBrushProperty, BlueLight, "FocusRing"));
            template.Triggers.Add(focus);

            return template;
        }

        // A focus-visual adorner (drawn over the focused control on keyboard focus):
        // a 2px BlueLight rounded border so text box / combo / list focus is visible.
        private static Style CreateFocusVisual()
        {
            var template = new ControlTemplate();
            var border = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            border.SetValue(System.Windows.Controls.Border.BorderBrushProperty, BlueLight);
            border.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(2));
            border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(System.Windows.Controls.Border.SnapsToDevicePixelsProperty, true);
            border.SetValue(FrameworkElement.MarginProperty, new Thickness(-2));
            template.VisualTree = border;
            var style = new Style();
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        // Maps a result's status word to its legend color so the review list is
        // colour-coded (eligible = red, kept = muted, deleted = green, protected =
        // blue, failed = amber). The colour is paired with the status word, never the
        // only signal, so meaning never depends on colour alone.
        private static Brush StatusToBrush(string statusLabel)
        {
            if (string.IsNullOrEmpty(statusLabel)) return Muted;
            switch (statusLabel)
            {
                case "Eligible": return RedText;
                case "Deleted": return Green;
                case "Protected": return BlueLight;
                case "Warning": return Amber;
                default: return Muted; // Kept / Ignored / NeverEmpty / etc.
            }
        }

        private void ApplyInitialWindowBounds()
        {
            double availableWidth = Math.Max(640d, SystemParameters.WorkArea.Width - 40d);
            double availableHeight = Math.Max(480d, SystemParameters.WorkArea.Height - 40d);

            MinWidth = Math.Min(PreferredMinWidth, availableWidth);
            MinHeight = Math.Min(PreferredMinHeight, availableHeight);
            Width = Math.Max(MinWidth, Math.Min(DefaultWindowWidth, availableWidth));
            Height = Math.Max(MinHeight, Math.Min(DefaultWindowHeight, availableHeight));
        }

        private void ApplyPhysicalStartupBounds()
        {
            if (appliedPhysicalStartupBounds)
            {
                return;
            }

            double scaleX;
            double scaleY;
            GetDpiScale(out scaleX, out scaleY);

            double availableWidth = Math.Max(640d, SystemParameters.WorkArea.Width - (40d / scaleX));
            double availableHeight = Math.Max(480d, SystemParameters.WorkArea.Height - (40d / scaleY));

            MinWidth = Math.Min(PreferredMinWidth, availableWidth);
            MinHeight = Math.Min(PreferredMinHeight, availableHeight);
            Width = Math.Max(MinWidth, Math.Min(DefaultWindowWidth, availableWidth));
            Height = Math.Max(MinHeight, Math.Min(DefaultWindowHeight, availableHeight));
            appliedPhysicalStartupBounds = true;
        }

        private void GetDpiScale(out double scaleX, out double scaleY)
        {
            scaleX = 1d;
            scaleY = 1d;
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
            {
                scaleX = source.CompositionTarget.TransformToDevice.M11;
                scaleY = source.CompositionTarget.TransformToDevice.M22;
            }
        }

        private void StartForwardWatcher()
        {
            try { if (File.Exists(Program.ForwardSignalPath)) File.Delete(Program.ForwardSignalPath); } catch { }
            forwardSignalTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            forwardSignalTimer.Tick += ForwardSignalTimer_Tick;
            forwardSignalTimer.Start();
        }

        private void ForwardSignalTimer_Tick(object sender, EventArgs e)
        {
            if (!File.Exists(Program.ForwardSignalPath))
            {
                return;
            }

            string path;
            try
            {
                path = File.ReadAllText(Program.ForwardSignalPath, System.Text.Encoding.UTF8).Trim();
                File.Delete(Program.ForwardSignalPath);
            }
            catch
            {
                return;
            }

            ProcessForwardedPath(path);
        }

        private void ProcessForwardedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();

            if (selectedTab != "Search")
            {
                selectedTab = "Search";
                RenderSelectedTab();
            }

            pathBox.Text = EnsureTrailingSlash(path);
            if (scanButton.IsEnabled)
            {
                StartScan();
            }
            else
            {
                detailStatusText.Text = "Received a folder from Explorer. Finish the current operation, then scan again.";
            }
        }

        private void BuildTitleBar()
        {
            var bar = new Border
            {
                Background = new LinearGradientBrush(ColorFrom("#101d2e"), ColorFrom("#07101b"), 0),
                BorderBrush = Border,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            Grid.SetRow(bar, 0);
            rootGrid.Children.Add(bar);

            var grid = new Grid { Margin = new Thickness(16, 0, 16, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.Child = grid;
            bar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    ToggleMaximize();
                }
                else
                {
                    DragMove();
                }
            };

            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(title, 0);
            title.Children.Add(new WpfImage
            {
                Source = ToImageSource(Properties.Resources.x128_Project),
                Width = 30,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0)
            });
            title.Children.Add(new TextBlock
            {
                Text = "RED++ - Remove Empty Directories+",
                Foreground = Text,
                FontSize = 19,
                VerticalAlignment = VerticalAlignment.Center
            });
            grid.Children.Add(title);

            var chrome = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(chrome, 2);
            chrome.Children.Add(ChromeButton(IconMinimize, "Minimize", (s, e) => WindowState = WindowState.Minimized));
            chrome.Children.Add(ChromeButton(IconMaximize, "Maximize or restore", (s, e) => ToggleMaximize()));
            chrome.Children.Add(ChromeButton(IconClose, "Close", (s, e) => Close()));
            grid.Children.Add(chrome);
        }

        private WpfButton ChromeButton(Geometry icon, string name, RoutedEventHandler click)
        {
            var button = new WpfButton
            {
                Content = IconPath(icon, Text, 18, 2.3),
                Width = 66,
                Height = 50,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Foreground = Text,
                Padding = new Thickness(0),
                Cursor = Cursors.Hand
            };
            SetAutomation(button, name);
            button.Click += click;
            return button;
        }

        private void BuildTabs()
        {
            var nav = new Border
            {
                Background = Bg2,
                BorderBrush = Border,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            Grid.SetRow(nav, 1);
            rootGrid.Children.Add(nav);

            tabPanel = new StackPanel { Orientation = Orientation.Horizontal };
            nav.Child = tabPanel;
            AddTab("Search", IconSearch);
            AddTab("Settings", IconSettings);
            AddTab("Filters", IconFilter);
            AddTab("About", IconInfo);
        }

        private void AddTab(string name, Geometry icon)
        {
            var button = new WpfButton
            {
                Height = 58,
                Width = 176,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 1, 0),
                BorderBrush = Border,
                Cursor = Cursors.Hand,
                Padding = new Thickness(0),
                Tag = name
            };
            SetAutomation(button, name + " tab");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var path = IconPath(icon, Muted, 28, 2.35);
            grid.Children.Add(path);
            var text = new TextBlock
            {
                Text = name,
                Foreground = Muted,
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            button.Content = grid;
            button.Click += (s, e) =>
            {
                selectedTab = name;
                RenderSelectedTab();
            };
            tabPanel.Children.Add(button);
        }

        private void BuildContent()
        {
            contentHost = new Grid { Margin = new Thickness(12, 14, 12, 12) };
            Grid.SetRow(contentHost, 2);
            rootGrid.Children.Add(contentHost);
            RenderSelectedTab();
        }

        private void RenderSelectedTab()
        {
            foreach (WpfButton tab in tabPanel.Children)
            {
                bool selected = (string)tab.Tag == selectedTab;
                tab.Background = selected ? Panel2 : Brushes.Transparent;
                var grid = (Grid)tab.Content;
                var path = (System.Windows.Shapes.Path)grid.Children[0];
                var text = (TextBlock)grid.Children[1];
                path.Stroke = selected ? Text : Muted;
                text.Foreground = selected ? Text : Muted;
                text.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
                tab.BorderThickness = selected ? new Thickness(1, 0, 1, 3) : new Thickness(0, 0, 1, 0);
                tab.BorderBrush = selected ? Blue : Border;
            }

            if (contentHost == null)
            {
                return;
            }

            UpdateConfigFromUi();
            contentHost.Children.Clear();
            if (selectedTab == "Settings")
            {
                contentHost.Children.Add(BuildSettingsTab());
                ApplyConfigToUi();
            }
            else if (selectedTab == "Filters") contentHost.Children.Add(BuildFiltersTab());
            else if (selectedTab == "About") contentHost.Children.Add(BuildAboutTab());
            else contentHost.Children.Add(BuildSearchTab());
        }

        private UIElement BuildSearchTab()
        {
            var group = Frame("Select Directory To Be Searched");
            var grid = new Grid { Margin = new Thickness(16, 20, 16, 14) };
            SetFrameContent(group, grid);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 620 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(246) });

            var pathRow = new Grid();
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumnSpan(pathRow, 3);
            grid.Children.Add(pathRow);

            pathBox = new WpfTextBox
            {
                Text = string.IsNullOrWhiteSpace(config.Volatile.LastUsedDirectory) ? @"C:\" : config.Volatile.LastUsedDirectory,
                Height = 40,
                FontSize = 17,
                Foreground = Text,
                Background = Bg,
                BorderBrush = BorderStrong,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 7, 12, 7),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            pathBox.FocusVisualStyle = FocusVisual;
            SetAutomation(pathBox, "Folder to scan", "Enter or paste the root folder RED++ should scan.");
            pathRow.Children.Add(pathBox);

            var browse = OutlineButton("Browse...", 150, 40);
            SetAutomation(browse, "Browse for folder", "Choose the root folder RED++ should scan.");
            browse.HorizontalAlignment = HorizontalAlignment.Stretch;
            browse.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(browse, 2);
            browse.Click += Browse_Click;
            pathRow.Children.Add(browse);

            resultSurface = new Border
            {
                Background = new LinearGradientBrush(ColorFrom("#0c1624"), ColorFrom("#142237"), 45),
                BorderBrush = BorderStrong,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 14, 0, 0)
            };
            Grid.SetRow(resultSurface, 1);
            grid.Children.Add(resultSurface);

            var surfaceGrid = new Grid();
            resultSurface.Child = surfaceGrid;
            resultsList = BuildResultsList();
            surfaceGrid.Children.Add(resultsList);
            emptyState = BuildEmptyState();
            surfaceGrid.Children.Add(emptyState);

            var legend = BuildLegend();
            Grid.SetRow(legend, 1);
            Grid.SetColumn(legend, 2);
            grid.Children.Add(legend);

            RefreshResultsVisibility();
            return group;
        }

        private Border BuildLegend()
        {
            var outer = new Border
            {
                Background = BrushFrom("#0b1420"),
                BorderBrush = Border,
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 14, 0, 0),
                Padding = new Thickness(14, 12, 14, 12)
            };
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var stack = new StackPanel();
            scroll.Content = stack;
            outer.Child = scroll;
            stack.Children.Add(new TextBlock
            {
                Text = "Result Legend",
                Foreground = Text,
                FontSize = 16,
                Margin = new Thickness(0, 0, 0, 10)
            });
            AddLegendRow(stack, IconHome, "Root", BrushFrom("#d7e6ff"));
            AddLegendRow(stack, IconFolder, "Empty", BrushFrom("#f3c95f"));
            AddLegendRow(stack, IconTrash, "Contains 'Trash'", BrushFrom("#79d59a"));
            AddLegendRow(stack, IconHidden, "Hidden", BrushFrom("#d6b323"), new DoubleCollection(new[] { 3d, 3d }));
            AddLegendRow(stack, IconLock, "Locked", BrushFrom("#d7b86a"));
            AddLegendRow(stack, IconNeverEmpty, "Never Empty", BrushFrom("#f3c95f"));
            AddLegendRow(stack, IconWarning, "Failed", BrushFrom("#ffca55"));
            AddLegendRow(stack, IconShield, "Protected", BlueLight);
            AddLegendRow(stack, IconCheck, "Deleted", Green);
            stack.Children.Add(new Border { Height = 1, Background = Border, Margin = new Thickness(0, 8, 0, 9) });
            AddSwatch(stack, BrushFrom("#59677e"), "Will not be deleted");
            AddSwatch(stack, Red, "Will be deleted");
            AddSwatch(stack, Blue, "Protected");
            return outer;
        }

        private void AddLegendRow(StackPanel stack, Geometry icon, string label, Brush brush, DoubleCollection dash = null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(IconPath(icon, brush, 18, 2.05, dash, new Thickness(0, 0, 12, 0)));
            row.Children.Add(new TextBlock { Text = label, Foreground = Muted, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            stack.Children.Add(row);
        }

        private void AddSwatch(StackPanel stack, Brush brush, string label)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };
            row.Children.Add(new Border
            {
                Width = 18,
                Height = 18,
                Background = brush,
                BorderBrush = BorderStrong,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 12, 0)
            });
            row.Children.Add(new TextBlock { Text = label, Foreground = Muted, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            stack.Children.Add(row);
        }

        private Grid BuildEmptyState()
        {
            var grid = new Grid();
            var center = new StackPanel
            {
                Width = 390,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(center);
            emptyIcon = IconPath(IconFolder, Muted2, 46, 2.5, new DoubleCollection(new[] { 5d, 4d }), new Thickness(0, 0, 0, 8));
            center.Children.Add(emptyIcon);
            emptyTitle = new TextBlock
            {
                Text = "Choose a folder to scan.",
                Foreground = Text,
                FontSize = 18,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            center.Children.Add(emptyTitle);
            emptySubtitle = new TextBlock
            {
                Text = "Review results before anything changes.",
                Foreground = Muted,
                FontSize = 14,
                LineHeight = 18,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            center.Children.Add(emptySubtitle);
            emptyTrust = new StackPanel();
            emptyTrust.Children.Add(TrustRow(IconSearch, BlueLight, "Pick a root folder, then scan."));
            emptyTrust.Children.Add(TrustRow(IconShield, Green, "Review eligible results."));
            emptyTrust.Children.Add(TrustRow(IconTrash, Pink, "Confirm before changes."));
            center.Children.Add(emptyTrust);
            return grid;
        }

        // Swaps the centre panel between the pre-scan prompt and a positive "all clean"
        // state, so a completed scan with no results no longer tells the user to pick a
        // folder they just scanned.
        private void SetEmptyStateMode(bool clean)
        {
            if (emptyIcon == null) return;
            if (clean)
            {
                emptyIcon.Data = IconCheck;
                emptyIcon.Stroke = Green;
                emptyIcon.StrokeDashArray = null;
                emptyTitle.Text = "No empty directories found.";
                emptySubtitle.Text = "RED++ scanned this folder and found nothing to remove.";
                emptyTrust.Visibility = Visibility.Collapsed;
            }
            else
            {
                emptyIcon.Data = IconFolder;
                emptyIcon.Stroke = Muted2;
                emptyIcon.StrokeDashArray = new DoubleCollection(new[] { 5d, 4d });
                emptyTitle.Text = "Choose a folder to scan.";
                emptySubtitle.Text = "Review results before anything changes.";
                emptyTrust.Visibility = Visibility.Visible;
            }
        }

        private UIElement TrustRow(Geometry icon, Brush brush, string text)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(58, 0, 0, 5)
            };
            row.Children.Add(IconPath(icon, brush, 16, 2.05, null, new Thickness(0, 0, 10, 0)));
            row.Children.Add(new TextBlock { Text = text, Foreground = Muted, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }

        private ListView BuildResultsList()
        {
            var list = new ListView
            {
                ItemsSource = results,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Text,
                Margin = new Thickness(8)
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
            SetAutomation(list, "Review results", "Empty directories and empty files found during the last scan.");
            list.FocusVisualStyle = FocusVisual;

            // Comfortable, tappable rows for a destructive-review list.
            var itemStyle = new Style(typeof(System.Windows.Controls.ListViewItem));
            itemStyle.Setters.Add(new Setter(Control.MinHeightProperty, 28d));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 2, 4, 2)));
            itemStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            list.ItemContainerStyle = itemStyle;

            var gridView = new GridView();
            list.View = gridView;

            // Status as a coloured word (eligible = red, kept = muted, deleted =
            // green, …) so the review list reflects the legend instead of leaving the
            // computed status colour unused. The word carries the meaning; the colour
            // reinforces it (never colour alone).
            var statusTemplate = new DataTemplate();
            var statusText = new FrameworkElementFactory(typeof(TextBlock));
            statusText.SetBinding(TextBlock.TextProperty, new Binding("StatusLabel"));
            statusText.SetBinding(TextBlock.ForegroundProperty, new Binding("StatusBrush"));
            statusText.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            statusTemplate.VisualTree = statusText;

            gridView.Columns.Add(new GridViewColumn { Header = "Status", Width = 96, CellTemplate = statusTemplate });
            gridView.Columns.Add(new GridViewColumn { Header = "Name", Width = 170, DisplayMemberBinding = new Binding("Name") });
            gridView.Columns.Add(new GridViewColumn { Header = "Reason", Width = 220, DisplayMemberBinding = new Binding("Reason") });
            gridView.Columns.Add(new GridViewColumn { Header = "Path", Width = 460, DisplayMemberBinding = new Binding("FullPath") });
            return list;
        }

        private UIElement BuildSettingsTab()
        {
            var group = Frame("Settings");
            var grid = new Grid { Margin = new Thickness(20, 28, 20, 20) };
            var scroll = new ScrollViewer
            {
                Content = grid,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            SetFrameContent(group, scroll);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var left = new StackPanel { Margin = new Thickness(0, 0, 28, 0) };
            grid.Children.Add(left);
            var right = new StackPanel { Margin = new Thickness(28, 0, 0, 0) };
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            ignoreEmptyFiles = SettingCheck(left, "Treat ignored files as removable trash", "Directories containing only configured ignored files can still be treated as empty.");
            ignoreSystem = SettingCheck(left, "Ignore system directories (recommended)", null);
            ignoreHidden = SettingCheck(left, "Ignore hidden directories", null);
            hideDeletionErrors = SettingCheck(left, "Continue past deletion errors", null);
            hideScanErrors = SettingCheck(left, "Hide scan errors in the result tree", null);
            hideIgnored = SettingCheck(left, "Hide ignored directories from results", null);
            protectRoot = SettingCheck(left, "Protect the starting directory", null);
            fastRendering = SettingCheck(left, "Fast result rendering", "Keeps the interface responsive on very large directory trees.");
            clipboardDetection = SettingCheck(left, "Detect folder paths in the clipboard", null);

            respectGitIgnore = SettingCheck(right, "Respect .gitignore rules during scans", null);
            useMft = SettingCheck(right, "Use MFT turbo scan (administrator only)", "Standard scan is used when administrator-only scan is unavailable.");
            deleteEmptyFiles = SettingCheck(right, "Include standalone zero-byte files", "Also review empty files that are not inside an empty directory.");
            right.Children.Add(Label("Deletion mode", 16, Text, FontWeights.SemiBold, new Thickness(0, 34, 0, 8)));
            deleteMode = new WpfComboBox
            {
                Height = 42,
                FontSize = 16,
                Background = Panel,
                Foreground = Text,
                BorderBrush = BorderStrong,
                BorderThickness = new Thickness(1)
            };
            deleteMode.FocusVisualStyle = FocusVisual;
            SetAutomation(deleteMode, "Deletion mode", "Choose whether RED++ simulates, recycles, deletes directly, or moves eligible results.");
            foreach (DeleteModes mode in DeleteModeItem.GetList())
            {
                deleteMode.Items.Add(new DeleteModeItem(mode));
            }
            right.Children.Add(deleteMode);
            return group;
        }

        private WpfCheckBox SettingCheck(StackPanel parent, string title, string helper)
        {
            var cb = new WpfCheckBox
            {
                Content = title,
                Foreground = Text,
                FontSize = 16,
                Margin = new Thickness(0, 0, 0, string.IsNullOrWhiteSpace(helper) ? 18 : 4)
            };
            SetAutomation(cb, title, helper);
            parent.Children.Add(cb);
            if (!string.IsNullOrWhiteSpace(helper))
            {
                parent.Children.Add(new TextBlock
                {
                    Text = helper,
                    Foreground = Muted,
                    FontSize = 14,
                    Margin = new Thickness(28, 0, 0, 18)
                });
            }
            return cb;
        }

        private UIElement BuildFiltersTab()
        {
            var group = Frame("Filters");
            var grid = new Grid { Margin = new Thickness(20) };
            var scroll = new ScrollViewer
            {
                Content = grid,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            SetFrameContent(group, scroll);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddFilterList(grid, 0, "Directories: Ignore", config.Filters.DirectoriesToIgnore);
            AddFilterList(grid, 1, "Directories: Never Empty", config.Filters.DirectoriesNeverEmpty);
            AddFilterList(grid, 2, "Files: Ignore", config.Filters.FilesToIgnore);
            return group;
        }

        private void AddFilterList(Grid grid, int column, string title, List<string> rules)
        {
            var panel = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 16, 0, column == 2 ? 0 : 16, 0) };
            Grid.SetColumn(panel, column);
            grid.Children.Add(panel);
            panel.Children.Add(Label(title, 18, Text, FontWeights.SemiBold, new Thickness(0, 0, 0, 12)));
            var list = new ListBox
            {
                ItemsSource = rules,
                Background = Bg,
                Foreground = Text,
                BorderBrush = Border,
                BorderThickness = new Thickness(1),
                MinHeight = 420,
                FontSize = 15
            };
            list.FocusVisualStyle = FocusVisual;
            panel.Children.Add(list);
        }

        private UIElement BuildAboutTab()
        {
            var group = Frame("About");
            var stack = new StackPanel { Margin = new Thickness(28) };
            SetFrameContent(group, stack);
            // Environment.ProcessPath (apphost exe) is single-file safe; Assembly.Location is empty in a bundle.
            FileVersionInfo vi = FileVersionInfo.GetVersionInfo(Environment.ProcessPath);
            stack.Children.Add(Label("RED++", 28, Text, FontWeights.SemiBold, new Thickness(0, 0, 0, 8)));
            stack.Children.Add(Label("Remove Empty Directories+ v" + vi.FileVersion, 18, Muted, FontWeights.Normal, new Thickness(0, 0, 0, 18)));
            stack.Children.Add(Label("Modern WPF shell using the existing RED++ scanner and deletion engine.", 16, Muted, FontWeights.Normal, new Thickness(0, 0, 0, 18)));
            stack.Children.Add(OutlineButton("Open project page", 180, 42, (s, e) => Process.Start("https://github.com/SysAdminDoc/REDplusplus/")));
            return group;
        }

        private void BuildCommandBar()
        {
            var bar = new Border
            {
                Background = Panel,
                BorderBrush = Border,
                BorderThickness = new Thickness(0, 1, 0, 1)
            };
            Grid.SetRow(bar, 3);
            rootGrid.Children.Add(bar);
            var grid = new Grid { Margin = new Thickness(20, 11, 20, 11) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.Child = grid;

            var primaryActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            grid.Children.Add(primaryActions);

            scanButton = ActionButton("Scan", Blue, IconSearch, 150);
            SetAutomation(scanButton, "Scan", "Scan the selected folder for empty directories and empty files.");
            scanButton.Click += (s, e) => StartScan();
            primaryActions.Children.Add(scanButton);

            deleteButton = ActionButton("Review & Delete", Red, IconTrash, 214);
            SetAutomation(deleteButton, "Review and delete", "Review eligible results and confirm before changing anything.");
            deleteButton.Margin = new Thickness(12, 0, 0, 0);
            deleteButton.Click += (s, e) => StartDelete();
            primaryActions.Children.Add(deleteButton);

            cancelButton = ActionButton("Cancel", Panel2, IconCancel, 140);
            SetAutomation(cancelButton, "Cancel current operation", "Cancel the scan or deletion currently in progress.");
            cancelButton.Margin = new Thickness(12, 0, 0, 0);
            cancelButton.Click += (s, e) => core?.CancelCurrentProcess();
            primaryActions.Children.Add(cancelButton);

            var secondaryActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(secondaryActions, 1);
            grid.Children.Add(secondaryActions);

            extrasButton = OutlineButton("Extras", 124, 54);
            SetAutomation(extrasButton, "Extras menu", "Open session log and export options.");
            extrasButton.Margin = new Thickness(12, 0, 0, 0);
            extrasButton.Click += (s, e) => ShowExtrasMenu();
            secondaryActions.Children.Add(extrasButton);

            exitButton = OutlineButton("Exit", 124, 54);
            SetAutomation(exitButton, "Exit RED++");
            exitButton.Margin = new Thickness(12, 0, 0, 0);
            exitButton.Click += (s, e) => Close();
            secondaryActions.Children.Add(exitButton);
        }

        private WpfButton ActionButton(string text, Brush background, Geometry icon, double width)
        {
            var button = new WpfButton
            {
                Width = width,
                Height = 52,
                Background = background,
                BorderBrush = BorderStrong,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(IconPath(icon, Text, 22, 2.45, null, new Thickness(0, 0, 10, 0)));
            row.Children.Add(new TextBlock { Text = text, Foreground = Text, FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            button.Content = row;
            return button;
        }

        private void BuildStatusBar()
        {
            var border = new Border
            {
                Background = Bg2,
                BorderBrush = Border,
                BorderThickness = new Thickness(0, 1, 0, 0)
            };
            Grid.SetRow(border, 4);
            rootGrid.Children.Add(border);

            var grid = new Grid { Margin = new Thickness(20, 0, 22, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            border.Child = grid;

            readyText = new TextBlock { Text = "●  Ready", Foreground = Text, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(readyText, 0);
            grid.Children.Add(readyText);
            itemCountText = new TextBlock { Text = "0 items", Foreground = Text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(itemCountText, 1);
            grid.Children.Add(itemCountText);
            detailStatusText = new TextBlock { Text = "Nothing to delete yet.", Foreground = Muted, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(detailStatusText, 2);
            grid.Children.Add(detailStatusText);
            progressText = new TextBlock { Text = "0%", Foreground = Text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(progressText, 3);
            grid.Children.Add(progressText);
            progressBar = new ProgressBar
            {
                Height = 12,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Background = Bg,
                Foreground = Blue,
                BorderBrush = Border,
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center
            };
            SetAutomation(progressBar, "Operation progress");
            Grid.SetColumn(progressBar, 4);
            grid.Children.Add(progressBar);
        }

        private Border Frame(string title)
        {
            var outer = new Border
            {
                Background = Panel,
                BorderBrush = Border,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0)
            };
            var grid = new Grid();
            outer.Child = grid;
            var titleBlock = new TextBlock
            {
                Text = title,
                Foreground = Text,
                Background = Panel,
                FontSize = 20,
                Margin = new Thickness(20, 12, 0, 0),
                Padding = new Thickness(4, 0, 10, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            var body = new Border
            {
                BorderBrush = Border,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(8, 28, 8, 8)
            };
            grid.Children.Add(body);
            grid.Children.Add(titleBlock);
            outer.Tag = body;
            return outer;
        }

        private static void SetFrameContent(Border frame, UIElement content)
        {
            var body = frame.Tag as Border;
            if (body != null)
            {
                body.Child = content;
            }
        }

        private TextBlock Label(string text, double size, Brush brush, FontWeight weight, Thickness margin)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = brush, FontWeight = weight, Margin = margin };
        }

        private static void SetAutomation(FrameworkElement element, string name, string helpText = null)
        {
            if (element == null || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            System.Windows.Automation.AutomationProperties.SetName(element, name);
            if (!string.IsNullOrWhiteSpace(helpText))
            {
                System.Windows.Automation.AutomationProperties.SetHelpText(element, helpText);
                element.ToolTip = helpText;
            }
        }

        private WpfButton OutlineButton(string text, double width, double height, RoutedEventHandler click = null)
        {
            var button = new WpfButton
            {
                Content = new TextBlock { Text = text, Foreground = Text, FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                Width = width,
                Height = height,
                Background = Panel2,
                BorderBrush = Border,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            if (click != null)
            {
                button.Click += click;
            }
            return button;
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new Forms.FolderBrowserDialog())
            {
                dlg.Description = "Select the folder RED++ will scan";
                dlg.ShowNewFolderButton = false;
                if (dlg.ShowDialog() == Forms.DialogResult.OK)
                {
                    pathBox.Text = EnsureTrailingSlash(dlg.SelectedPath);
                }
            }
        }

        private void StartScan()
        {
            DirectoryInfo selectedDirectory;
            if (!TryGetSelectedDirectory(out selectedDirectory))
            {
                return;
            }

            pathBox.Text = EnsureTrailingSlash(selectedDirectory.FullName);
            if (runData != null)
            {
                runData.Dispose();
            }
            runData = CreateRuntimeData(selectedDirectory);
            core = new REDCore(runData);
            AttachCoreEvents(core);
            results.Clear();
            rowsByPath.Clear();
            RefreshResultsVisibility();
            runtimeWatch.Restart();
            runData.AddLogSpacer();
            runData.AddLogMessage("Scanning for empty directories...");
            UpdateUiState(true);
            readyText.Text = "●  Busy";
            detailStatusText.Text = "Scanning for empty directories...";
            progressBar.IsIndeterminate = true;
            core.SearchingForEmptyDirectories();
        }

        private bool TryGetSelectedDirectory(out DirectoryInfo selectedDirectory)
        {
            selectedDirectory = null;
            string rawPath = pathBox == null ? string.Empty : pathBox.Text;
            rawPath = string.IsNullOrWhiteSpace(rawPath) ? string.Empty : rawPath.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                WpfMessageBox.Show(this, "Choose an existing local, UNC, or network folder before scanning.", "RED++", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            try
            {
                string expandedPath = Environment.ExpandEnvironmentVariables(rawPath);
                string fullPath = Path.GetFullPath(expandedPath);
                selectedDirectory = new DirectoryInfo(fullPath);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is IOException || ex is UnauthorizedAccessException)
            {
                WpfMessageBox.Show(this, "That folder path is not valid.\n\n" + ex.Message, "RED++", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!selectedDirectory.Exists)
            {
                WpfMessageBox.Show(this, "Choose an existing local, UNC, or network folder before scanning.", "RED++", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private RuntimeData CreateRuntimeData(DirectoryInfo selectedDirectory)
        {
            UpdateConfigFromUi();
            var data = new RuntimeData();
            data.StartFolder = selectedDirectory;
            data.HideDeletionErrors = config.Options.HideDeletionErrors;
            data.HideScanErrors = config.Options.HideScanErrors;
            data.IgnoreEmptyFiles = config.Options.IgnoreEmptyFiles;
            data.IgnoreHiddenFolders = config.Options.IgnoreHiddenDirectories;
            data.IgnoreSystemFolders = config.Options.IgnoreSystemDirectories;
            data.MinFolderAgeHours = config.Options.MinDirectoryAgeHours;
            data.MaxDepth = config.Options.MaxDirectoryDepth;
            data.InfiniteLoopDetectionCount = config.Options.InfiniteLoopDetectionCount;
            data.DeleteMode = (DeleteModes)config.Options.DeleteMode;
            data.PauseTime = config.Options.PauseBetweenDeletions;
            data.HideIgnoredDirectories = config.Options.HideIgnoredDirectories;
            data.RespectGitIgnore = config.Options.RespectGitIgnore;
            data.UseMftScan = config.Options.UseMftScan;
            data.DeleteEmptyFiles = config.Options.DeleteEmptyFiles;
            data.IgnoreFileNameList.Transform(config.Filters.FilesToIgnore);
            data.IgnoreDirectoryNameList.Transform(config.Filters.DirectoriesToIgnore);
            data.NeverEmptyDirectoryList.Transform(config.Filters.DirectoriesNeverEmpty);
            return data;
        }

        private void AttachCoreEvents(REDCore activeCore)
        {
            activeCore.OnProgressChanged += (s, e) => Dispatcher.Invoke(() => detailStatusText.Text = Convert.ToString(e.UserState));
            activeCore.OnFoundEmptyDirectory += (s, e) => Dispatcher.Invoke(() => AddOrUpdateResult(e.ScanResult));
            activeCore.OnFinishedScanForEmptyDirs += (s, e) => Dispatcher.Invoke(() =>
            {
                runtimeWatch.Stop();
                if (config.Options.AutoProtectRoot && runData.StartFolder != null)
                {
                    activeCore.AddProtectedFolder(runData.StartFolder.FullName);
                }
                AddEmptyFileResults();
                int total = e.EmptyFolderCount + e.EmptyFileCount;
                detailStatusText.Text = total == 0
                    ? string.Format("Checked {0} {1} — no empty directories found.", e.FolderCount, e.FolderCount == 1 ? "directory" : "directories")
                    : string.Format("{0} empty directories and {1} empty files eligible.", e.EmptyFolderCount, e.EmptyFileCount);
                itemCountText.Text = total + " items";
                hasScanned = true;
                UpdateUiState(false);
                deleteButton.IsEnabled = total > 0;
                progressBar.IsIndeterminate = false;
                progressBar.Value = 0;
                progressText.Text = "0%";
                RefreshResultsVisibility();
            });
            activeCore.OnError += (s, e) => Dispatcher.Invoke(() =>
            {
                UpdateUiState(false);
                WpfMessageBox.Show(this, e.Message, "RED++ Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
            activeCore.OnCancelled += (s, e) => Dispatcher.Invoke(() =>
            {
                UpdateUiState(false);
                detailStatusText.Text = "Canceled.";
            });
            activeCore.OnAborted += (s, e) => Dispatcher.Invoke(() =>
            {
                UpdateUiState(false);
                detailStatusText.Text = "Stopped after an error.";
            });
            activeCore.OnDeleteProcessChanged += (s, e) => Dispatcher.Invoke(() =>
            {
                if (e == null) return;
                AddOrUpdateResult(e.ScanResult);
                if (rowsByPath.ContainsKey(e.ScanResult.FullPath))
                {
                    ResultRow updatedRow = rowsByPath[e.ScanResult.FullPath];
                    updatedRow.StatusLabel = e.Status.ToString();
                    updatedRow.StatusBrush = StatusToBrush(updatedRow.StatusLabel);
                }
                progressBar.IsIndeterminate = false;
                progressBar.Maximum = Math.Max(1, e.FolderCount);
                progressBar.Value = Math.Min(progressBar.Maximum, e.ProgressStatus + 1);
                progressText.Text = Math.Round(progressBar.Value * 100d / progressBar.Maximum).ToString("0") + "%";
            });
            activeCore.OnDeleteError += (s, e) => Dispatcher.Invoke(() =>
            {
                var response = WpfMessageBox.Show(this,
                    "RED++ could not change this item. The item was left unchanged.\n\n" + e.Path + "\n\n" + e.ErrorMessage + "\n\nContinue with the next item?",
                    "Deletion needs attention",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (response == MessageBoxResult.Yes)
                {
                    activeCore.ContinueDeleteProcess();
                }
                else
                {
                    activeCore.AbortDeletion();
                }
            });
            activeCore.OnDeleteProcessFinished += (s, e) => Dispatcher.Invoke(() =>
            {
                UpdateUiState(false);
                deleteButton.IsEnabled = false;
                detailStatusText.Text = BuildCompletionMessage(e.DeletedFolderCount, e.DeletedFileCount);
                progressBar.IsIndeterminate = false;
                progressBar.Value = 0;
                progressText.Text = "0%";
            });
        }

        private void AddOrUpdateResult(RedScanResultItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.FullPath))
            {
                return;
            }

            ResultRow row;
            if (!rowsByPath.TryGetValue(item.FullPath, out row))
            {
                row = new ResultRow();
                rowsByPath[item.FullPath] = row;
                results.Add(row);
            }
            row.Name = item.Name;
            row.FullPath = item.FullPath;
            row.Reason = item.StatusReason;
            row.StatusLabel = item.SearchStatus == DirectorySearchStatusTypes.Empty ? "Eligible" : "Kept";
            row.StatusBrush = StatusToBrush(row.StatusLabel);
            itemCountText.Text = results.Count + " items";
            RefreshResultsVisibility();
        }

        private void AddEmptyFileResults()
        {
            if (runData == null || runData.EmptyFileResults == null)
            {
                return;
            }

            foreach (FileInfo file in runData.EmptyFileResults)
            {
                AddOrUpdateResult(new RedScanResultItem(file, DirectorySearchStatusTypes.Empty, "Empty file - zero bytes"));
            }
        }

        private void StartDelete()
        {
            if (core == null || runData == null)
            {
                return;
            }

            UpdateConfigFromUi();
            ApplyCurrentDeleteSettings();
            if (!EnsureMoveToFolderTarget())
            {
                return;
            }

            if (runData.DeleteMode != DeleteModes.Simulate)
            {
                int protectedCount = runData.ProtectedFolderList.Count;
                int deleteCount = Math.Max(0, runData.ScanResults.Count - protectedCount);
                int fileDeleteCount = runData.EmptyFileResults.Count;
                string message = BuildDeleteConfirmationMessage(deleteCount, fileDeleteCount, protectedCount);
                if (WpfMessageBox.Show(this, message, "Review & Delete", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            UpdateUiState(true);
            deleteButton.IsEnabled = false;
            progressBar.IsIndeterminate = false;
            progressBar.Value = 0;
            string deleteMsg = "Deletion started. RED++ will re-check each item before changing it.";
            DeleteModes activeMode = (DeleteModes)config.Options.DeleteMode;
            if ((int)activeMode <= (int)DeleteModes.RecycleBinWithQuestion
                && runData != null && runData.StartFolder != null
                && RED.Helper.RedAssist.IsNoRecycleBinPath(runData.StartFolder.FullName))
            {
                // No Recycle Bin on UNC/network/removable: be honest that this is permanent.
                deleteMsg = "Note: this location has no Recycle Bin (network/removable) - deletion is permanent. Undo can still recreate the empty directories.";
            }
            detailStatusText.Text = deleteMsg;
            core.StartDeleteProcess();
        }

        private void ApplyCurrentDeleteSettings()
        {
            if (runData == null)
            {
                return;
            }

            runData.DeleteMode = (DeleteModes)config.Options.DeleteMode;
            runData.HideDeletionErrors = config.Options.HideDeletionErrors;
            runData.PauseTime = config.Options.PauseBetweenDeletions;
        }

        private bool EnsureMoveToFolderTarget()
        {
            if (runData == null || runData.DeleteMode != DeleteModes.MoveToFolder)
            {
                return true;
            }

            using (var dlg = new Forms.FolderBrowserDialog())
            {
                dlg.Description = "Select the folder where eligible empty directories and empty files will be moved";
                dlg.ShowNewFolderButton = true;
                if (dlg.ShowDialog() != Forms.DialogResult.OK)
                {
                    detailStatusText.Text = "Move-to-folder deletion canceled. Choose a move target to continue.";
                    return false;
                }

                SystemFunctions.MoveToFolderTarget = dlg.SelectedPath;
                return true;
            }
        }

        private string BuildDeleteConfirmationMessage(int deleteCount, int fileDeleteCount, int protectedCount)
        {
            string countSummary = string.Format("{0} empty directories and {1} empty files are eligible.", deleteCount, fileDeleteCount);
            string safety = "RED++ will re-check every item immediately before changing it.";
            if (protectedCount > 0)
            {
                countSummary += "\n" + string.Format("{0} protected directories will be skipped.", protectedCount);
            }

            switch (runData.DeleteMode)
            {
                case DeleteModes.MoveToFolder:
                    return "Move eligible results to the selected folder?\n\n"
                        + countSummary + "\n"
                        + safety + "\n"
                        + "Move target: " + SystemFunctions.MoveToFolderTarget;
                case DeleteModes.Direct:
                    return "Permanently delete eligible results?\n\n"
                        + countSummary + "\n"
                        + "Direct mode bypasses the Recycle Bin.\n"
                        + safety;
                default:
                    return "Recycle eligible results?\n\n"
                        + countSummary + "\n"
                        + "Windows will move items to the Recycle Bin when available.\n"
                        + safety;
            }
        }

        // Completion copy that matches the chosen mode. Critically, a dry run reports
        // "would be removed / nothing was changed" instead of claiming files changed.
        private string BuildCompletionMessage(int dirs, int files)
        {
            string d = dirs == 1 ? "directory" : "directories";
            string f = files == 1 ? "file" : "files";
            DeleteModes mode = runData != null ? runData.DeleteMode : DeleteModes.RecycleBin;
            switch (mode)
            {
                case DeleteModes.Simulate:
                    return string.Format("Dry run complete — {0} {1} and {2} {3} would be removed. Nothing was changed.", dirs, d, files, f);
                case DeleteModes.MoveToFolder:
                    return string.Format("Moved {0} {1} and {2} {3}.", dirs, d, files, f);
                case DeleteModes.Direct:
                    return string.Format("Deleted {0} {1} and {2} {3}.", dirs, d, files, f);
                default:
                    return string.Format("Recycled {0} {1} and {2} {3}.", dirs, d, files, f);
            }
        }

        private void UpdateUiState(bool busy)
        {
            scanButton.IsEnabled = !busy;
            cancelButton.IsEnabled = busy;
            extrasButton.IsEnabled = !busy;
            readyText.Text = busy ? "●  Working…" : "●  Ready";
            readyText.Foreground = busy ? Amber : Green;
            // Screen readers should hear the state word, not the decorative bullet.
            System.Windows.Automation.AutomationProperties.SetName(readyText, busy ? "Working" : "Ready");
            if (!busy && deleteButton != null)
            {
                // Enable delete only when something is actually eligible — rows that
                // were merely kept (protected/never-empty) must not arm the button.
                deleteButton.IsEnabled = EligibleResultCount() > 0;
            }
        }

        private int EligibleResultCount()
        {
            int count = 0;
            foreach (ResultRow row in results)
            {
                if (string.Equals(row.StatusLabel, "Eligible", StringComparison.Ordinal))
                {
                    count++;
                }
            }
            return count;
        }

        private void RefreshResultsVisibility()
        {
            if (emptyState == null || resultsList == null)
            {
                return;
            }
            bool empty = results.Count == 0;
            emptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            resultsList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            if (empty) SetEmptyStateMode(hasScanned);
        }

        private void ApplyConfigToUi()
        {
            if (ignoreEmptyFiles == null)
            {
                return;
            }
            ignoreEmptyFiles.IsChecked = config.Options.IgnoreEmptyFiles;
            ignoreSystem.IsChecked = config.Options.IgnoreSystemDirectories;
            ignoreHidden.IsChecked = config.Options.IgnoreHiddenDirectories;
            hideDeletionErrors.IsChecked = config.Options.HideDeletionErrors;
            hideScanErrors.IsChecked = config.Options.HideScanErrors;
            hideIgnored.IsChecked = config.Options.HideIgnoredDirectories;
            protectRoot.IsChecked = config.Options.AutoProtectRoot;
            fastRendering.IsChecked = config.Options.FastSearchMode;
            clipboardDetection.IsChecked = config.Options.ClipboardPathDetection;
            respectGitIgnore.IsChecked = config.Options.RespectGitIgnore;
            useMft.IsChecked = config.Options.UseMftScan;
            deleteEmptyFiles.IsChecked = config.Options.DeleteEmptyFiles;
            if (deleteMode != null && deleteMode.Items.Count > 0)
            {
                int index = Math.Max(0, Math.Min(deleteMode.Items.Count - 1, config.Options.DeleteModeInt));
                deleteMode.SelectedIndex = index;
            }
        }

        private void UpdateConfigFromUi()
        {
            if (pathBox != null)
            {
                config.Volatile.LastUsedDirectory = pathBox.Text;
            }

            if (ignoreEmptyFiles == null)
            {
                return;
            }
            config.Options.IgnoreEmptyFiles = ignoreEmptyFiles.IsChecked == true;
            config.Options.IgnoreSystemDirectories = ignoreSystem.IsChecked == true;
            config.Options.IgnoreHiddenDirectories = ignoreHidden.IsChecked == true;
            config.Options.HideDeletionErrors = hideDeletionErrors.IsChecked == true;
            config.Options.HideScanErrors = hideScanErrors.IsChecked == true;
            config.Options.HideIgnoredDirectories = hideIgnored.IsChecked == true;
            config.Options.AutoProtectRoot = protectRoot.IsChecked == true;
            config.Options.FastSearchMode = fastRendering.IsChecked == true;
            config.Options.ClipboardPathDetection = clipboardDetection.IsChecked == true;
            config.Options.RespectGitIgnore = respectGitIgnore.IsChecked == true;
            config.Options.UseMftScan = useMft.IsChecked == true;
            config.Options.DeleteEmptyFiles = deleteEmptyFiles.IsChecked == true;
            if (deleteMode != null && deleteMode.SelectedItem is DeleteModeItem item)
            {
                config.Options.DeleteModeInt = (int)item.DeleteMode;
            }
        }

        private void ShowLog()
        {
            string log = core == null ? "No log entries for this session yet." : core.GetLogMessages();
            var win = new Window
            {
                Owner = this,
                Title = "RED++ Log",
                Width = 780,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Bg,
                Content = new WpfTextBox
                {
                    Text = log,
                    IsReadOnly = true,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 14,
                    Foreground = Text,
                    Background = Bg,
                    BorderBrush = Border,
                    TextWrapping = TextWrapping.NoWrap,
                    AcceptsReturn = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
                }
            };
            win.ShowDialog();
        }

        private void ShowExtrasMenu()
        {
            var menu = new ContextMenu
            {
                PlacementTarget = extrasButton,
                Placement = PlacementMode.Top,
                Background = Panel2,
                BorderBrush = Border,
                Foreground = Text
            };

            bool hasResults = results.Count > 0;

            menu.Items.Add(BuildRestoreMenu());
            menu.Items.Add(ExtrasMenuItem("Import saved dry-run results...", true, (s, e) => ImportDryRunResults()));
            menu.Items.Add(new Separator());
            menu.Items.Add(ExtrasMenuItem("View log", true, (s, e) => ShowLog()));
            menu.Items.Add(new Separator());
            menu.Items.Add(ExtrasMenuItem("Export results to file...", hasResults, (s, e) => ExportResultsToFile()));
            menu.Items.Add(ExtrasMenuItem("Copy results to clipboard", hasResults, (s, e) => ExportResultsToClipboard()));
            menu.IsOpen = true;
        }

        // "Restore deletion" submenu: one entry per kept undo manifest (newest
        // first), disabled when there is nothing to restore. Mirrors the classic
        // shell so the recovery workflow no longer requires -classic.
        private MenuItem BuildRestoreMenu()
        {
            List<UndoManager.ManifestInfo> manifests;
            try { manifests = UndoManager.ListManifests(); }
            catch { manifests = new List<UndoManager.ManifestInfo>(); }

            var parent = new MenuItem
            {
                Header = "Restore deletion",
                IsEnabled = manifests.Count > 0,
                Foreground = Text,
                Background = Panel2,
                Padding = new Thickness(14, 8, 18, 8)
            };
            SetAutomation(parent, "Restore deletion", "Restore directories and empty files from a previous deletion run.");

            foreach (UndoManager.ManifestInfo info in manifests)
            {
                string label = string.Format("{0}  ({1}, {2} item{3})",
                    info.Timestamp.ToString("g"), info.DeleteMode, info.EntryCount,
                    info.EntryCount == 1 ? "" : "s");
                string path = info.FilePath;
                var item = new MenuItem
                {
                    Header = label,
                    Foreground = Text,
                    Background = Panel2,
                    Padding = new Thickness(14, 8, 18, 8)
                };
                SetAutomation(item, "Restore deletion from " + info.Timestamp.ToString("g"));
                item.Click += (s, e) => RestoreFromManifest(path);
                parent.Items.Add(item);
            }
            return parent;
        }

        private void RestoreFromManifest(string manifestPath)
        {
            UpdateUiState(true);
            progressBar.IsIndeterminate = true;
            detailStatusText.Text = "Restoring deleted items...";

            System.Threading.Tasks.Task.Run(() =>
            {
                int restored = 0, failed = 0;
                Exception error = null;
                try
                {
                    UndoManager.Restore(manifestPath, out restored, out failed,
                        msg => Dispatcher.Invoke(() => detailStatusText.Text = msg));
                }
                catch (Exception ex) { error = ex; }

                Dispatcher.Invoke(() =>
                {
                    UpdateUiState(false);
                    progressBar.IsIndeterminate = false;
                    if (error != null)
                    {
                        detailStatusText.Text = "Restore failed.";
                        WpfMessageBox.Show(this, "RED++ could not restore the selected run.\n\n" + error.Message,
                            "Restore failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    detailStatusText.Text = failed == 0
                        ? string.Format("Restored {0} item{1}.", restored, restored == 1 ? "" : "s")
                        : string.Format("Restored {0}, failed {1}. See the log for details.", restored, failed);
                });
            });
        }

        private void ImportDryRunResults()
        {
            string fileName;
            using (var dlg = new Forms.OpenFileDialog())
            {
                dlg.Title = "Import Saved Dry-Run Results";
                dlg.Filter = "Dry-run results (*.json;*.ndjson;*.csv;*.txt)|*.json;*.ndjson;*.csv;*.txt|All files (*.*)|*.*";
                if (dlg.ShowDialog() != Forms.DialogResult.OK) { return; }
                fileName = dlg.FileName;
            }

            RED.Helper.RedImportedScanResults imported;
            try
            {
                imported = RED.Helper.RedImportScanResults.ReadFile(fileName);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(this, "RED++ could not read that file.\n\n" + ex.Message,
                    "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (imported == null || imported.ReviewCount == 0)
            {
                detailStatusText.Text = "No reviewable records found in that file.";
                return;
            }

            // Imported records are for review/export only in the modern shell: load
            // them into the results list. Eligible (Empty) rows show as Eligible;
            // everything else shows as Kept. Re-scan to delete (the engine re-checks
            // every directory before acting regardless).
            results.Clear();
            rowsByPath.Clear();
            core = null;
            runData = null;

            if (selectedTab != "Search")
            {
                selectedTab = "Search";
                RenderSelectedTab();
            }

            int eligible = 0;
            foreach (RED.Helper.RedImportedScanRoot root in imported.Roots)
            {
                foreach (RedScanResultItem item in root.Results)
                {
                    AddOrUpdateResult(item);
                    if (item.SearchStatus == DirectorySearchStatusTypes.Empty) { eligible++; }
                }
            }

            RefreshResultsVisibility();
            detailStatusText.Text = string.Format(
                "Imported {0} record{1} from {2}. {3} eligible. Re-scan the folder to delete.",
                imported.ReviewCount, imported.ReviewCount == 1 ? "" : "s",
                Path.GetFileName(fileName), eligible);
        }

        private MenuItem ExtrasMenuItem(string label, bool enabled, RoutedEventHandler click)
        {
            var item = new MenuItem
            {
                Header = label,
                IsEnabled = enabled,
                Foreground = Text,
                Background = Panel2,
                Padding = new Thickness(14, 8, 18, 8)
            };
            item.Click += click;
            SetAutomation(item, label);
            return item;
        }

        private void ExportResultsToFile()
        {
            List<string> paths = BuildReviewPathList();
            if (paths.Count == 0)
            {
                detailStatusText.Text = "Scan results are needed before export.";
                return;
            }

            try
            {
                using (var export = new RED.Helper.RedExportScanResults())
                {
                    export.ExportToFile(paths);
                }
                detailStatusText.Text = "Export finished.";
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(this, "RED++ could not export the current results.\n\n" + ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExportResultsToClipboard()
        {
            List<string> paths = BuildReviewPathList();
            if (paths.Count == 0)
            {
                detailStatusText.Text = "Scan results are needed before export.";
                return;
            }

            try
            {
                using (var export = new RED.Helper.RedExportScanResults())
                {
                    export.ExportToClipboard(paths);
                }
                detailStatusText.Text = "Results copied to the clipboard.";
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(this, "RED++ could not copy the current results.\n\n" + ex.Message, "Copy failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private List<string> BuildReviewPathList()
        {
            var paths = new List<string>();
            foreach (ResultRow row in results)
            {
                if (!string.IsNullOrWhiteSpace(row.FullPath))
                {
                    paths.Add(row.FullPath);
                }
            }
            return paths;
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private static string EnsureTrailingSlash(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                return path;
            }
            return path + Path.DirectorySeparatorChar;
        }

        private static Brush BrushFrom(string hex)
        {
            return new SolidColorBrush(ColorFrom(hex));
        }

        private static Geometry Glyph(string data)
        {
            var geometry = Geometry.Parse(data);
            geometry.Freeze();
            return geometry;
        }

        private static System.Windows.Shapes.Path IconPath(Geometry geometry, Brush brush, double size, double strokeThickness, DoubleCollection dash = null, Thickness? margin = null)
        {
            var path = new System.Windows.Shapes.Path
            {
                Data = geometry,
                Stroke = brush,
                StrokeThickness = strokeThickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = Brushes.Transparent,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = margin ?? new Thickness(0)
            };
            if (dash != null)
            {
                path.StrokeDashArray = dash;
            }
            return path;
        }

        private static Color ColorFrom(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        private static ImageSource ToImageSource(System.Drawing.Icon icon)
        {
            return Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
        }

        private static ImageSource ToImageSource(System.Drawing.Image image)
        {
            if (image == null)
            {
                return null;
            }
            using (var bitmap = new System.Drawing.Bitmap(image))
            {
                IntPtr hBitmap = bitmap.GetHbitmap();
                try
                {
                    return Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private sealed class ResultRow : INotifyPropertyChanged
        {
            private string name;
            private string fullPath;
            private string statusLabel;
            private string reason;
            private Brush statusBrush;

            public event PropertyChangedEventHandler PropertyChanged;

            public string Name { get { return name; } set { name = value; Changed("Name"); } }
            public string FullPath { get { return fullPath; } set { fullPath = value; Changed("FullPath"); } }
            public string StatusLabel { get { return statusLabel; } set { statusLabel = value; Changed("StatusLabel"); } }
            public string Reason { get { return reason; } set { reason = value; Changed("Reason"); } }
            public Brush StatusBrush { get { return statusBrush; } set { statusBrush = value; Changed("StatusBrush"); } }

            private void Changed(string propertyName)
            {
                var handler = PropertyChanged;
                if (handler != null)
                {
                    handler(this, new PropertyChangedEventArgs(propertyName));
                }
            }
        }
    }
}
