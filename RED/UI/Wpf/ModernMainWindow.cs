using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

        private Grid rootGrid;
        private Grid contentHost;
        private Border resultSurface;
        private Grid emptyState;
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
        private bool appliedStartupDpiSize;

        private const double MockupPixelWidth = 1584d;
        private const double MockupPixelHeight = 992d;
        private const double MinPixelWidth = 1180d;
        private const double MinPixelHeight = 720d;

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
        private static readonly Brush Green = BrushFrom("#67d16f");
        private static readonly Brush Pink = BrushFrom("#f17aa5");

        public ModernMainWindow(string startPath, bool shouldAutoSearch)
        {
            initialPath = startPath;
            autoSearch = shouldAutoSearch;
            Title = "RED++ - Remove Empty Directories+";
            Width = MockupPixelWidth;
            Height = MockupPixelHeight;
            MinWidth = MinPixelWidth;
            MinHeight = MinPixelHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Background = Bg;
            Icon = ToImageSource(Properties.Resources.iconProject);

            LoadConfig();
            BuildUi();
            ApplyConfigToUi();
            UpdateUiState(false);

            SourceInitialized += (s, e) => ApplyStartupDpiWindowSize();
            SizeChanged += (s, e) => ApplyDpiCompensation();
            Loaded += (s, e) =>
            {
                ApplyDpiCompensation();
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
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(110) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
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

            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.42));
            disabled.Setters.Add(new Setter(Control.BackgroundProperty, BrushFrom("#1b2435")));
            disabled.Setters.Add(new Setter(Control.BorderBrushProperty, BrushFrom("#34445c")));
            style.Triggers.Add(disabled);

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BorderBrushProperty, BrushFrom("#7a8aa5")));
            style.Triggers.Add(hover);

            var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focus.Setters.Add(new Setter(Control.BorderBrushProperty, BlueLight));
            style.Triggers.Add(focus);

            return style;
        }

        private static ControlTemplate CreateButtonTemplate()
        {
            var template = new ControlTemplate(typeof(WpfButton));
            var border = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            border.Name = "ButtonChrome";
            border.SetValue(System.Windows.Controls.Border.SnapsToDevicePixelsProperty, true);
            border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(2));
            border.SetBinding(System.Windows.Controls.Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(System.Windows.Controls.Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(System.Windows.Controls.Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.SnapsToDevicePixelsProperty, true);
            presenter.SetBinding(ContentPresenter.ContentProperty, new Binding("Content") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("ContentTemplate") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetBinding(ContentPresenter.HorizontalAlignmentProperty, new Binding("HorizontalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetBinding(ContentPresenter.VerticalAlignmentProperty, new Binding("VerticalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
            border.AppendChild(presenter);
            template.VisualTree = border;
            return template;
        }

        private void ApplyStartupDpiWindowSize()
        {
            if (appliedStartupDpiSize)
            {
                return;
            }

            double scaleX;
            double scaleY;
            GetDpiScale(out scaleX, out scaleY);
            MinWidth = MinPixelWidth / scaleX;
            MinHeight = MinPixelHeight / scaleY;
            Width = MockupPixelWidth / scaleX;
            Height = MockupPixelHeight / scaleY;
            appliedStartupDpiSize = true;
        }

        private void ApplyDpiCompensation()
        {
            if (rootGrid == null || ActualWidth <= 0 || ActualHeight <= 0)
            {
                return;
            }

            double scaleX = 1d;
            double scaleY = 1d;
            GetDpiScale(out scaleX, out scaleY);

            rootGrid.Width = ActualWidth * scaleX;
            rootGrid.Height = ActualHeight * scaleY;
            rootGrid.LayoutTransform = new ScaleTransform(1d / scaleX, 1d / scaleY);
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
                Source = ToImageSource(Properties.Resources.iconProject),
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 8, 0)
            });
            title.Children.Add(new TextBlock
            {
                Text = "RED++ - Remove Empty Directories+",
                Foreground = Text,
                FontSize = 21,
                VerticalAlignment = VerticalAlignment.Center
            });
            grid.Children.Add(title);

            var chrome = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(chrome, 2);
            chrome.Children.Add(ChromeButton("−", (s, e) => WindowState = WindowState.Minimized));
            chrome.Children.Add(ChromeButton("□", (s, e) => ToggleMaximize()));
            chrome.Children.Add(ChromeButton("×", (s, e) => Close()));
            grid.Children.Add(chrome);
        }

        private WpfButton ChromeButton(string text, RoutedEventHandler click)
        {
            var button = new WpfButton
            {
                Content = text,
                Width = 76,
                Height = 54,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Foreground = Text,
                FontSize = 24,
                Padding = new Thickness(0),
                Cursor = Cursors.Hand
            };
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
            AddTab("Search", Geometry.Parse("M10,10 A8,8 0 1 1 22,22 M20,20 L30,30"));
            AddTab("Settings", Geometry.Parse("M18,4 L20,10 L26,11 L22,16 L24,22 L18,20 L12,22 L14,16 L10,11 L16,10 Z M18,13 A5,5 0 1 1 17.9,13"));
            AddTab("Filters", Geometry.Parse("M7,6 L31,6 L22,17 L22,29 L16,32 L16,17 Z"));
            AddTab("About", Geometry.Parse("M18,5 A13,13 0 1 1 17.9,5 M18,16 L18,27 M18,11 L18,11"));
        }

        private void AddTab(string name, Geometry icon)
        {
            var button = new WpfButton
            {
                Height = 64,
                Width = 182,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 1, 0),
                BorderBrush = Border,
                Cursor = Cursors.Hand,
                Padding = new Thickness(0),
                Tag = name
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var path = new System.Windows.Shapes.Path
            {
                Data = icon,
                Stroke = Muted,
                StrokeThickness = 2.4,
                Stretch = Stretch.Uniform,
                Width = 30,
                Height = 30,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            grid.Children.Add(path);
            var text = new TextBlock
            {
                Text = name,
                Foreground = Muted,
                FontSize = 20,
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

            contentHost.Children.Clear();
            if (selectedTab == "Settings") contentHost.Children.Add(BuildSettingsTab());
            else if (selectedTab == "Filters") contentHost.Children.Add(BuildFiltersTab());
            else if (selectedTab == "About") contentHost.Children.Add(BuildAboutTab());
            else contentHost.Children.Add(BuildSearchTab());
        }

        private UIElement BuildSearchTab()
        {
            var group = Frame("Select Directory To Be Searched");
            var grid = new Grid { Margin = new Thickness(20, 24, 20, 20) };
            SetFrameContent(group, grid);

            pathBox = new WpfTextBox
            {
                Text = string.IsNullOrWhiteSpace(config.Volatile.LastUsedDirectory) ? @"C:\" : config.Volatile.LastUsedDirectory,
                Height = 48,
                FontSize = 21,
                Foreground = Text,
                Background = Bg,
                BorderBrush = BorderStrong,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 9, 12, 8),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 480, 0)
            };
            grid.Children.Add(pathBox);

            var browse = OutlineButton("Browse...", 170, 44);
            browse.HorizontalAlignment = HorizontalAlignment.Right;
            browse.VerticalAlignment = VerticalAlignment.Top;
            browse.Margin = new Thickness(0, 3, 300, 0);
            browse.Click += Browse_Click;
            grid.Children.Add(browse);

            resultSurface = new Border
            {
                Background = new LinearGradientBrush(ColorFrom("#0c1624"), ColorFrom("#142237"), 45),
                BorderBrush = BorderStrong,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 68, 300, 0)
            };
            grid.Children.Add(resultSurface);

            var surfaceGrid = new Grid();
            resultSurface.Child = surfaceGrid;
            resultsList = BuildResultsList();
            surfaceGrid.Children.Add(resultsList);
            emptyState = BuildEmptyState();
            surfaceGrid.Children.Add(emptyState);

            var legend = BuildLegend();
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
                Width = 264,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 22, 10, 0),
                Padding = new Thickness(24, 18, 24, 18)
            };
            var stack = new StackPanel();
            outer.Child = stack;
            stack.Children.Add(new TextBlock
            {
                Text = "Result Legend",
                Foreground = Text,
                FontSize = 20,
                Margin = new Thickness(0, 0, 0, 18)
            });
            AddLegendRow(stack, Properties.Resources.x16_home, "Root");
            AddLegendRow(stack, Properties.Resources.x16_folder, "Empty");
            AddLegendRow(stack, Properties.Resources.x16_recyclebin1, "Contains 'Trash'");
            AddLegendRow(stack, null, "Hidden", BrushFrom("#d6b323"));
            AddLegendRow(stack, Properties.Resources.x16_protected, "Locked");
            AddLegendRow(stack, Properties.Resources.x16_folder_ne, "Never Empty");
            AddLegendRow(stack, Properties.Resources.x24_warning1, "Failed");
            AddLegendRow(stack, Properties.Resources.x16_Shield1, "Protected");
            AddLegendRow(stack, null, "Deleted", Green);
            stack.Children.Add(new Border { Height = 1, Background = Border, Margin = new Thickness(0, 28, 0, 22) });
            AddSwatch(stack, BrushFrom("#59677e"), "Will not be deleted");
            AddSwatch(stack, Red, "Will be deleted");
            AddSwatch(stack, Blue, "Protected");
            return outer;
        }

        private void AddLegendRow(StackPanel stack, System.Drawing.Image image, string label, Brush outlineBrush = null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 13) };
            if (image != null)
            {
                row.Children.Add(new WpfImage { Source = ToImageSource(image), Width = 26, Height = 26, Margin = new Thickness(0, 0, 16, 0) });
            }
            else
            {
                row.Children.Add(new Border
                {
                    Width = 26,
                    Height = 22,
                    Margin = new Thickness(0, 2, 16, 0),
                    BorderBrush = outlineBrush ?? Border,
                    BorderThickness = new Thickness(2),
                    Background = Brushes.Transparent
                });
            }
            row.Children.Add(new TextBlock { Text = label, Foreground = Muted, FontSize = 19, VerticalAlignment = VerticalAlignment.Center });
            stack.Children.Add(row);
        }

        private void AddSwatch(StackPanel stack, Brush brush, string label)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            row.Children.Add(new Border
            {
                Width = 24,
                Height = 24,
                Background = brush,
                BorderBrush = BorderStrong,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 18, 0)
            });
            row.Children.Add(new TextBlock { Text = label, Foreground = Muted, FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
            stack.Children.Add(row);
        }

        private Grid BuildEmptyState()
        {
            var grid = new Grid();
            var center = new StackPanel
            {
                Width = 520,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(center);
            center.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M10,28 L10,76 L96,76 L96,24 L60,24 L50,12 L27,12 L18,28 Z"),
                Stroke = Muted2,
                StrokeThickness = 4,
                StrokeDashArray = new DoubleCollection(new[] { 5d, 4d }),
                Fill = Brushes.Transparent,
                Width = 96,
                Height = 78,
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 22)
            });
            center.Children.Add(new TextBlock
            {
                Text = "Choose a folder to scan.",
                Foreground = Text,
                FontSize = 24,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            });
            center.Children.Add(new TextBlock
            {
                Text = "RED++ shows reviewable results before\nanything is deleted.",
                Foreground = Muted,
                FontSize = 20,
                LineHeight = 27,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 30)
            });
            center.Children.Add(TrustRow(Geometry.Parse("M10,10 A8,8 0 1 1 22,22 M20,20 L30,30"), BlueLight, "Pick a root folder, then scan."));
            center.Children.Add(TrustRow(Geometry.Parse("M16,2 L28,8 L25,24 L16,32 L7,24 L4,8 Z M10,16 L14,20 L23,11"), Green, "Results are shown for review."));
            center.Children.Add(TrustRow(Geometry.Parse("M10,11 L26,11 M13,11 L13,29 L23,29 L23,11 M15,7 L21,7 M16,15 L16,25 M20,15 L20,25"), Pink, "Nothing is deleted until you confirm."));
            return grid;
        }

        private UIElement TrustRow(Geometry icon, Brush brush, string text)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(112, 0, 0, 18)
            };
            row.Children.Add(new System.Windows.Shapes.Path
            {
                Data = icon,
                Stroke = brush,
                StrokeThickness = 2.6,
                Width = 28,
                Height = 28,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 18, 0)
            });
            row.Children.Add(new TextBlock { Text = text, Foreground = Muted, FontSize = 19, VerticalAlignment = VerticalAlignment.Center });
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
            var gridView = new GridView();
            list.View = gridView;
            gridView.Columns.Add(new GridViewColumn { Header = "Status", Width = 130, DisplayMemberBinding = new Binding("StatusLabel") });
            gridView.Columns.Add(new GridViewColumn { Header = "Name", Width = 220, DisplayMemberBinding = new Binding("Name") });
            gridView.Columns.Add(new GridViewColumn { Header = "Reason", Width = 320, DisplayMemberBinding = new Binding("Reason") });
            gridView.Columns.Add(new GridViewColumn { Header = "Path", Width = 760, DisplayMemberBinding = new Binding("FullPath") });
            return list;
        }

        private UIElement BuildSettingsTab()
        {
            var group = Frame("Settings");
            var grid = new Grid { Margin = new Thickness(20, 28, 20, 20) };
            SetFrameContent(group, grid);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var left = new StackPanel { Margin = new Thickness(0, 0, 28, 0) };
            grid.Children.Add(left);
            var right = new StackPanel { Margin = new Thickness(28, 0, 0, 0) };
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            ignoreEmptyFiles = SettingCheck(left, "Treat zero-byte files as empty", "Directories containing only zero-byte files can be treated as empty.");
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
            SetFrameContent(group, grid);
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
            panel.Children.Add(list);
        }

        private UIElement BuildAboutTab()
        {
            var group = Frame("About");
            var stack = new StackPanel { Margin = new Thickness(28) };
            SetFrameContent(group, stack);
            FileVersionInfo vi = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
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
            var grid = new Grid { Margin = new Thickness(32, 20, 32, 20) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.Child = grid;

            scanButton = ActionButton("Scan", Blue, Geometry.Parse("M10,10 A8,8 0 1 1 22,22 M20,20 L30,30"));
            scanButton.Click += (s, e) => StartScan();
            grid.Children.Add(scanButton);

            deleteButton = ActionButton("Review & Delete", Red, Geometry.Parse("M10,11 L26,11 M13,11 L13,29 L23,29 L23,11 M15,7 L21,7 M16,15 L16,25 M20,15 L20,25"));
            deleteButton.Margin = new Thickness(20, 0, 0, 0);
            deleteButton.Click += (s, e) => StartDelete();
            Grid.SetColumn(deleteButton, 1);
            grid.Children.Add(deleteButton);

            cancelButton = ActionButton("Cancel", BrushFrom("#27334a"), Geometry.Parse("M9,9 L27,27 M27,9 L9,27"));
            cancelButton.Margin = new Thickness(20, 0, 0, 0);
            cancelButton.Click += (s, e) => core?.CancelCurrentProcess();
            Grid.SetColumn(cancelButton, 2);
            grid.Children.Add(cancelButton);

            extrasButton = OutlineButton("Extras", 200, 68);
            extrasButton.Margin = new Thickness(0, 0, 34, 0);
            extrasButton.Click += (s, e) => ShowLog();
            Grid.SetColumn(extrasButton, 4);
            grid.Children.Add(extrasButton);

            exitButton = OutlineButton("Exit", 216, 68);
            exitButton.Click += (s, e) => Close();
            Grid.SetColumn(exitButton, 5);
            grid.Children.Add(exitButton);
        }

        private WpfButton ActionButton(string text, Brush background, Geometry icon)
        {
            var button = new WpfButton
            {
                Width = text.StartsWith("Review") ? 296 : 238,
                Height = 68,
                Background = background,
                BorderBrush = BrushFrom("#6f86ad"),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new System.Windows.Shapes.Path
            {
                Data = icon,
                Stroke = Text,
                StrokeThickness = 2.6,
                Width = 28,
                Height = 28,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 18, 0)
            });
            row.Children.Add(new TextBlock { Text = text, Foreground = Text, FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
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

            var grid = new Grid { Margin = new Thickness(24, 0, 28, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(420) });
            border.Child = grid;

            readyText = new TextBlock { Text = "●  Ready", Foreground = Text, FontSize = 19, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(readyText, 0);
            grid.Children.Add(readyText);
            itemCountText = new TextBlock { Text = "0 items", Foreground = Text, FontSize = 17, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(itemCountText, 1);
            grid.Children.Add(itemCountText);
            detailStatusText = new TextBlock { Text = "Nothing to delete yet.", Foreground = Muted, FontSize = 17, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(detailStatusText, 2);
            grid.Children.Add(detailStatusText);
            progressText = new TextBlock { Text = "0%", Foreground = Text, FontSize = 17, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(progressText, 3);
            grid.Children.Add(progressText);
            progressBar = new ProgressBar
            {
                Height = 18,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Background = Bg,
                Foreground = Blue,
                BorderBrush = Border,
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center
            };
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

        private WpfButton OutlineButton(string text, double width, double height, RoutedEventHandler click = null)
        {
            var button = new WpfButton
            {
                Content = new TextBlock { Text = text, Foreground = Text, FontSize = 19, HorizontalAlignment = HorizontalAlignment.Center },
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
            var selectedDirectory = new DirectoryInfo(Environment.ExpandEnvironmentVariables(pathBox.Text.Trim('"')));
            if (!selectedDirectory.Exists)
            {
                WpfMessageBox.Show(this, "Choose an existing local, UNC, or network folder before scanning.", "RED++", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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
                int total = e.EmptyFolderCount + e.EmptyFileCount;
                detailStatusText.Text = total == 0
                    ? string.Format("Checked {0} directories. Nothing to delete yet.", e.FolderCount)
                    : string.Format("{0} empty directories and {1} empty files eligible.", e.EmptyFolderCount, e.EmptyFileCount);
                itemCountText.Text = total + " items";
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
                detailStatusText.Text = "Operation canceled.";
            });
            activeCore.OnAborted += (s, e) => Dispatcher.Invoke(() =>
            {
                UpdateUiState(false);
                detailStatusText.Text = "Operation stopped.";
            });
            activeCore.OnDeleteProcessChanged += (s, e) => Dispatcher.Invoke(() =>
            {
                if (e == null) return;
                AddOrUpdateResult(e.ScanResult);
                if (rowsByPath.ContainsKey(e.ScanResult.FullPath))
                {
                    rowsByPath[e.ScanResult.FullPath].StatusLabel = e.Status.ToString();
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
                detailStatusText.Text = string.Format("Deletion complete: {0} directories and {1} files changed.", e.DeletedFolderCount, e.DeletedFileCount);
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
            row.StatusBrush = item.SearchStatus == DirectorySearchStatusTypes.Empty ? Red : Muted;
            itemCountText.Text = results.Count + " items";
            RefreshResultsVisibility();
        }

        private void StartDelete()
        {
            if (core == null || runData == null)
            {
                return;
            }

            UpdateConfigFromUi();
            if (runData.DeleteMode != DeleteModes.Simulate)
            {
                int protectedCount = runData.ProtectedFolderList.Count;
                int deleteCount = runData.ScanResults.Count - protectedCount;
                int fileDeleteCount = runData.EmptyFileResults.Count;
                string message = string.Format("{0} empty directories and {1} empty files are eligible.\n\nRED++ will re-check every item immediately before changing it.", deleteCount, fileDeleteCount);
                if (WpfMessageBox.Show(this, message, "Review & Delete", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            UpdateUiState(true);
            deleteButton.IsEnabled = false;
            progressBar.IsIndeterminate = false;
            progressBar.Value = 0;
            detailStatusText.Text = "Deletion started. RED++ will re-check each item before changing it.";
            core.StartDeleteProcess();
        }

        private void UpdateUiState(bool busy)
        {
            scanButton.IsEnabled = !busy;
            cancelButton.IsEnabled = busy;
            extrasButton.IsEnabled = !busy;
            readyText.Text = busy ? "●  Busy" : "●  Ready";
            readyText.Foreground = busy ? BrushFrom("#e8c35b") : Text;
            if (!busy && deleteButton != null)
            {
                deleteButton.IsEnabled = results.Count > 0;
            }
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
            if (deleteMode != null && deleteMode.Items.Count > 0)
            {
                int index = Math.Max(0, Math.Min(deleteMode.Items.Count - 1, config.Options.DeleteModeInt));
                deleteMode.SelectedIndex = index;
            }
        }

        private void UpdateConfigFromUi()
        {
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
            if (deleteMode != null && deleteMode.SelectedItem is DeleteModeItem item)
            {
                config.Options.DeleteModeInt = (int)item.DeleteMode;
            }
            config.Volatile.LastUsedDirectory = pathBox == null ? config.Volatile.LastUsedDirectory : pathBox.Text;
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
