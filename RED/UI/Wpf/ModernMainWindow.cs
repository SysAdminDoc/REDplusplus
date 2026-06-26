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

        private static readonly FontFamily UiFont = new FontFamily("Segoe UI Variable Text, Segoe UI");
        private static readonly Brush Bg = BrushFrom("#0f141b");
        private static readonly Brush Bg2 = BrushFrom("#121922");
        private static readonly Brush Panel = BrushFrom("#172130");
        private static readonly Brush Panel2 = BrushFrom("#1e2a3a");
        private static readonly Brush Surface = BrushFrom("#0d1219");
        private static readonly Brush SurfaceRaised = BrushFrom("#111b28");
        private static readonly Brush Border = BrushFrom("#2d3a4d");
        private static readonly Brush BorderStrong = BrushFrom("#4d5f78");
        private static readonly Brush Text = BrushFrom("#edf2fb");
        private static readonly Brush Muted = BrushFrom("#a9b6ca");
        private static readonly Brush Muted2 = BrushFrom("#77859a");
        private static readonly Brush Blue = BrushFrom("#3f7cf5");
        private static readonly Brush BlueLight = BrushFrom("#91b7ff");
        private static readonly Brush Red = BrushFrom("#ef4554");
        // A brighter red than the legend swatch, for status text that must stay legible
        // on the dark result surface (the #dc3548 swatch dips below AA at body size).
        private static readonly Brush RedText = BrushFrom("#ff8792");
        private static readonly Brush Green = BrushFrom("#6ed17b");
        private static readonly Brush Amber = BrushFrom("#f6c75a");
        private static readonly Brush Pink = BrushFrom("#f08ab1");

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
            FontFamily = UiFont;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
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
                if (core != null)
                {
                    try { core.CancelCurrentProcess(); } catch { }
                    core = null;
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
            rootGrid = new Grid { Background = Bg, SnapsToDevicePixels = true, UseLayoutRounding = true };
            rootGrid.Resources.Add(typeof(WpfButton), CreateButtonStyle());
            rootGrid.Resources.Add(typeof(WpfCheckBox), CreateCheckBoxStyle());
            rootGrid.Resources.Add(typeof(WpfComboBox), CreateComboBoxStyle());
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });
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
            border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(6));
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
            overlay.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(6));
            overlay.SetValue(System.Windows.Controls.Border.BackgroundProperty, Brushes.Transparent);
            overlay.SetValue(UIElement.IsHitTestVisibleProperty, false);
            layers.AppendChild(overlay);

            // Keyboard focus ring — an inner border that is transparent until the
            // button takes keyboard focus, so it is visible on any background.
            var focusRing = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            focusRing.Name = "FocusRing";
            focusRing.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(6));
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

        private static Style CreateCheckBoxStyle()
        {
            var style = new Style(typeof(WpfCheckBox));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 15d));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateCheckBoxTemplate()));

            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.46));
            style.Triggers.Add(disabled);
            return style;
        }

        private static ControlTemplate CreateCheckBoxTemplate()
        {
            var template = new ControlTemplate(typeof(WpfCheckBox));
            var dock = new FrameworkElementFactory(typeof(DockPanel));
            dock.SetValue(DockPanel.LastChildFillProperty, true);

            var box = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            box.Name = "CheckBoxChrome";
            box.SetValue(FrameworkElement.WidthProperty, 18d);
            box.SetValue(FrameworkElement.HeightProperty, 18d);
            box.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 10, 0));
            box.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(4));
            box.SetValue(System.Windows.Controls.Border.BackgroundProperty, Surface);
            box.SetValue(System.Windows.Controls.Border.BorderBrushProperty, BorderStrong);
            box.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(1));
            box.SetValue(System.Windows.Controls.Border.SnapsToDevicePixelsProperty, true);
            box.SetValue(DockPanel.DockProperty, Dock.Left);

            var check = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            check.Name = "CheckMark";
            check.SetValue(System.Windows.Shapes.Path.DataProperty, IconCheck);
            check.SetValue(System.Windows.Shapes.Path.StrokeProperty, Text);
            check.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 3d);
            check.SetValue(System.Windows.Shapes.Path.StrokeStartLineCapProperty, PenLineCap.Round);
            check.SetValue(System.Windows.Shapes.Path.StrokeEndLineCapProperty, PenLineCap.Round);
            check.SetValue(System.Windows.Shapes.Path.StrokeLineJoinProperty, PenLineJoin.Round);
            check.SetValue(System.Windows.Shapes.Path.FillProperty, Brushes.Transparent);
            check.SetValue(FrameworkElement.WidthProperty, 11d);
            check.SetValue(FrameworkElement.HeightProperty, 11d);
            check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            check.SetValue(System.Windows.Shapes.Path.StretchProperty, Stretch.Uniform);
            check.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            box.AppendChild(check);

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.SnapsToDevicePixelsProperty, true);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetBinding(ContentPresenter.ContentProperty, new Binding("Content") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("ContentTemplate") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetBinding(ContentPresenter.ContentStringFormatProperty, new Binding("ContentStringFormat") { RelativeSource = RelativeSource.TemplatedParent });

            dock.AppendChild(box);
            dock.AppendChild(presenter);
            template.VisualTree = dock;

            var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckMark"));
            checkedTrigger.Setters.Add(new Setter(System.Windows.Controls.Border.BackgroundProperty, Blue, "CheckBoxChrome"));
            checkedTrigger.Setters.Add(new Setter(System.Windows.Controls.Border.BorderBrushProperty, BlueLight, "CheckBoxChrome"));
            template.Triggers.Add(checkedTrigger);

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(System.Windows.Controls.Border.BorderBrushProperty, BlueLight, "CheckBoxChrome"));
            template.Triggers.Add(hover);

            var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focus.Setters.Add(new Setter(System.Windows.Controls.Border.BorderBrushProperty, BlueLight, "CheckBoxChrome"));
            focus.Setters.Add(new Setter(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(2), "CheckBoxChrome"));
            template.Triggers.Add(focus);

            return template;
        }

        private static Style CreateComboBoxStyle()
        {
            var style = new Style(typeof(WpfComboBox));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Surface));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, BorderStrong));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 0, 10, 0)));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateComboBoxTemplate()));
            return style;
        }

        private static ControlTemplate CreateComboBoxTemplate()
        {
            var template = new ControlTemplate(typeof(WpfComboBox));
            var grid = new FrameworkElementFactory(typeof(Grid));

            var toggle = new FrameworkElementFactory(typeof(ToggleButton));
            toggle.Name = "ComboToggle";
            toggle.SetValue(UIElement.FocusableProperty, false);
            toggle.SetValue(Control.ForegroundProperty, Text);
            toggle.SetValue(ButtonBase.ClickModeProperty, ClickMode.Press);
            toggle.SetValue(Control.TemplateProperty, CreateContentOnlyToggleTemplate());
            toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsDropDownOpen")
            {
                RelativeSource = RelativeSource.TemplatedParent,
                Mode = BindingMode.TwoWay
            });

            var chrome = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            chrome.Name = "ComboChrome";
            chrome.SetValue(System.Windows.Controls.Border.BackgroundProperty, Surface);
            chrome.SetValue(System.Windows.Controls.Border.BorderBrushProperty, BorderStrong);
            chrome.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(1));
            chrome.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(6));
            chrome.SetValue(System.Windows.Controls.Border.SnapsToDevicePixelsProperty, true);

            var chromeGrid = new FrameworkElementFactory(typeof(Grid));
            chromeGrid.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.Name = "ContentSite";
            content.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 0, 36, 0));
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            content.SetValue(UIElement.IsHitTestVisibleProperty, false);
            content.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, Text);
            content.SetBinding(ContentPresenter.ContentProperty, new Binding("SelectionBoxItem") { RelativeSource = RelativeSource.TemplatedParent });
            content.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("SelectionBoxItemTemplate") { RelativeSource = RelativeSource.TemplatedParent });
            content.SetBinding(ContentPresenter.ContentStringFormatProperty, new Binding("SelectionBoxItemStringFormat") { RelativeSource = RelativeSource.TemplatedParent });
            chromeGrid.AppendChild(content);

            var arrow = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            arrow.SetValue(System.Windows.Shapes.Path.DataProperty, Glyph("M10,14 L18,22 L26,14"));
            arrow.SetValue(System.Windows.Shapes.Path.StrokeProperty, Muted);
            arrow.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 2.2d);
            arrow.SetValue(System.Windows.Shapes.Path.StrokeStartLineCapProperty, PenLineCap.Round);
            arrow.SetValue(System.Windows.Shapes.Path.StrokeEndLineCapProperty, PenLineCap.Round);
            arrow.SetValue(System.Windows.Shapes.Path.StrokeLineJoinProperty, PenLineJoin.Round);
            arrow.SetValue(System.Windows.Shapes.Path.FillProperty, Brushes.Transparent);
            arrow.SetValue(FrameworkElement.WidthProperty, 16d);
            arrow.SetValue(FrameworkElement.HeightProperty, 16d);
            arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 12, 0));
            arrow.SetValue(System.Windows.Shapes.Path.StretchProperty, Stretch.Uniform);
            chromeGrid.AppendChild(arrow);

            chrome.AppendChild(chromeGrid);
            toggle.AppendChild(chrome);
            grid.AppendChild(toggle);

            var popup = new FrameworkElementFactory(typeof(Popup));
            popup.Name = "Popup";
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(UIElement.FocusableProperty, false);
            popup.SetBinding(Popup.IsOpenProperty, new Binding("IsDropDownOpen") { RelativeSource = RelativeSource.TemplatedParent });

            var dropBorder = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            dropBorder.SetValue(System.Windows.Controls.Border.BackgroundProperty, Panel2);
            dropBorder.SetValue(System.Windows.Controls.Border.BorderBrushProperty, BorderStrong);
            dropBorder.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(1));
            dropBorder.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(6));
            dropBorder.SetBinding(FrameworkElement.MinWidthProperty, new Binding("ActualWidth") { RelativeSource = RelativeSource.TemplatedParent });
            dropBorder.SetValue(FrameworkElement.MaxHeightProperty, 260d);

            var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
            scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            var items = new FrameworkElementFactory(typeof(ItemsPresenter));
            scroll.AppendChild(items);
            dropBorder.AppendChild(scroll);
            popup.AppendChild(dropBorder);
            grid.AppendChild(popup);

            template.VisualTree = grid;

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(System.Windows.Controls.Border.BorderBrushProperty, BlueLight, "ComboChrome"));
            template.Triggers.Add(hover);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focus.Setters.Add(new Setter(System.Windows.Controls.Border.BorderBrushProperty, BlueLight, "ComboChrome"));
            focus.Setters.Add(new Setter(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(2), "ComboChrome"));
            template.Triggers.Add(focus);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.46));
            template.Triggers.Add(disabled);

            return template;
        }

        private static ControlTemplate CreateContentOnlyToggleTemplate()
        {
            var template = new ControlTemplate(typeof(ToggleButton));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.SnapsToDevicePixelsProperty, true);
            presenter.SetBinding(ContentPresenter.ContentProperty, new Binding("Content") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("ContentTemplate") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetBinding(ContentPresenter.HorizontalAlignmentProperty, new Binding("HorizontalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetBinding(ContentPresenter.VerticalAlignmentProperty, new Binding("VerticalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
            template.VisualTree = presenter;
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
                Background = new LinearGradientBrush(ColorFrom("#141d29"), ColorFrom("#0b1119"), 0),
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
                Width = 32,
                Height = 32,
                Margin = new Thickness(0, 0, 11, 0)
            });
            var titleText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleText.Children.Add(new TextBlock
            {
                Text = "RED++",
                Foreground = Text,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold
            });
            titleText.Children.Add(new TextBlock
            {
                Text = "Remove Empty Directories+",
                Foreground = Muted,
                FontSize = 12,
                Margin = new Thickness(0, 1, 0, 0)
            });
            title.Children.Add(titleText);
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
                Width = 58,
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
                Height = 54,
                Width = 156,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 1, 0),
                BorderBrush = Border,
                Cursor = Cursors.Hand,
                Padding = new Thickness(0),
                Tag = name
            };
            SetAutomation(button, name + " tab");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var path = IconPath(icon, Muted, 24, 2.25);
            grid.Children.Add(path);
            var text = new TextBlock
            {
                Text = name,
                Foreground = Muted,
                FontSize = 15,
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
                tab.BorderThickness = selected ? new Thickness(1, 0, 1, 2) : new Thickness(0, 0, 1, 0);
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
            var group = Frame("Scan Target");
            var grid = new Grid { Margin = new Thickness(18, 18, 18, 16) };
            SetFrameContent(group, grid);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 560 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(254) });

            var pathRow = new Grid();
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumnSpan(pathRow, 3);
            grid.Children.Add(pathRow);

            pathBox = new WpfTextBox
            {
                Text = string.IsNullOrWhiteSpace(config.Volatile.LastUsedDirectory) ? @"C:\" : config.Volatile.LastUsedDirectory,
                Height = 42,
                FontSize = 16,
                Foreground = Text,
                Background = Surface,
                BorderBrush = BorderStrong,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 8, 12, 8),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                CaretBrush = Text,
                SelectionBrush = Blue
            };
            pathBox.FocusVisualStyle = FocusVisual;
            pathBox.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter && scanButton != null && scanButton.IsEnabled)
                    StartScan();
            };
            SetAutomation(pathBox, "Folder to scan", "Enter or paste the root folder RED++ should scan.");
            pathRow.Children.Add(pathBox);

            var browse = OutlineButton("Browse...", 150, 42, null, IconFolder);
            SetAutomation(browse, "Browse for folder", "Choose the root folder RED++ should scan.");
            browse.HorizontalAlignment = HorizontalAlignment.Stretch;
            browse.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(browse, 2);
            browse.Click += Browse_Click;
            pathRow.Children.Add(browse);

            var helper = new TextBlock
            {
                Text = "Local paths, UNC shares, and environment variables are supported. RED++ always reviews results before changing anything.",
                Foreground = Muted,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 8, 0, 0)
            };
            Grid.SetRow(helper, 1);
            Grid.SetColumnSpan(helper, 3);
            grid.Children.Add(helper);

            resultSurface = new Border
            {
                Background = new LinearGradientBrush(ColorFrom("#0d1219"), ColorFrom("#132033"), 45),
                BorderBrush = BorderStrong,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 14, 0, 0)
            };
            Grid.SetRow(resultSurface, 2);
            grid.Children.Add(resultSurface);

            var surfaceGrid = new Grid();
            resultSurface.Child = surfaceGrid;
            resultsList = BuildResultsList();
            surfaceGrid.Children.Add(resultsList);
            emptyState = BuildEmptyState();
            surfaceGrid.Children.Add(emptyState);

            var legend = BuildLegend();
            Grid.SetRow(legend, 2);
            Grid.SetColumn(legend, 2);
            grid.Children.Add(legend);

            RefreshResultsVisibility();
            return group;
        }

        private Border BuildLegend()
        {
            var outer = new Border
            {
                Background = Surface,
                BorderBrush = Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 14, 0, 0),
                Padding = new Thickness(14, 12, 14, 12)
            };
            var stack = new StackPanel();
            outer.Child = stack;
            stack.Children.Add(new TextBlock
            {
                Text = "Review Guide",
                Foreground = Text,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            stack.Children.Add(new TextBlock
            {
                Text = "Status text is the source of truth. Color and icons reinforce it.",
                Foreground = Muted,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 11)
            });
            stack.Children.Add(Label("Outcomes", 12, Muted2, FontWeights.SemiBold, new Thickness(0, 0, 0, 7)));
            AddLegendRow(stack, IconTrash, "Eligible", RedText);
            AddLegendRow(stack, IconFolder, "Kept / skipped", Muted);
            AddLegendRow(stack, IconShield, "Protected", BlueLight);
            AddLegendRow(stack, IconCheck, "Deleted", Green);
            AddLegendRow(stack, IconWarning, "Warning / failed", Amber);
            stack.Children.Add(new Border { Height = 1, Background = Border, Margin = new Thickness(0, 8, 0, 9) });
            stack.Children.Add(Label("Safety", 12, Muted2, FontWeights.SemiBold, new Thickness(0, 0, 0, 7)));
            AddLegendRow(stack, IconHome, "Root protected", BlueLight);
            AddLegendRow(stack, IconLock, "Locked unchanged", Amber);
            stack.Children.Add(Label("RED++ re-checks every item immediately before deleting or moving it.", 12, Muted, FontWeights.Normal, new Thickness(0, 7, 0, 0)));
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
                Width = 430,
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
                FontSize = 19,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            center.Children.Add(emptyTitle);
            emptySubtitle = new TextBlock
            {
                Text = "Start with a local folder, network share, or path from the clipboard.",
                Foreground = Muted,
                FontSize = 14,
                LineHeight = 19,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            center.Children.Add(emptySubtitle);
            emptyTrust = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            emptyTrust.Children.Add(TrustRow(IconSearch, BlueLight, "Pick a root folder, then scan."));
            emptyTrust.Children.Add(TrustRow(IconShield, Green, "Review every eligible result."));
            emptyTrust.Children.Add(TrustRow(IconTrash, Pink, "Confirm before any change is made."));
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
                emptySubtitle.Text = "The scan completed with the active filters and no filesystem changes were made.";
                emptyTrust.Visibility = Visibility.Collapsed;
            }
            else
            {
                emptyIcon.Data = IconFolder;
                emptyIcon.Stroke = Muted2;
                emptyIcon.StrokeDashArray = new DoubleCollection(new[] { 5d, 4d });
                emptyTitle.Text = "Choose a folder to scan.";
                emptySubtitle.Text = "Start with a local folder, network share, or path from the clipboard.";
                emptyTrust.Visibility = Visibility.Visible;
            }
        }

        private UIElement TrustRow(Geometry icon, Brush brush, string text)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 6)
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
            list.Resources.Add(SystemColors.HighlightBrushKey, Panel2);
            list.Resources.Add(SystemColors.HighlightTextBrushKey, Text);
            list.Resources.Add(SystemColors.InactiveSelectionHighlightBrushKey, SurfaceRaised);
            list.Resources.Add(SystemColors.InactiveSelectionHighlightTextBrushKey, Text);
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
            // A whole-volume scan can return hundreds of thousands of rows. The ListView
            // virtualizes by default; tune it for that scale — recycle containers instead
            // of creating/destroying one per scroll, and defer realization until the
            // scrollbar thumb is released. (Row state binds to the ResultRow view-model,
            // never the container, so recycling cannot leak state across rows.)
            VirtualizingPanel.SetIsVirtualizing(list, true);
            VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            ScrollViewer.SetIsDeferredScrollingEnabled(list, true);
            SetAutomation(list, "Review results", "Empty directories and empty files found during the last scan.");
            list.FocusVisualStyle = FocusVisual;

            list.ItemContainerStyle = CreateResultItemStyle();

            var gridView = new GridView();
            gridView.ColumnHeaderContainerStyle = CreateGridHeaderStyle();
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
            statusText.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 8, 0));
            statusText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            statusTemplate.VisualTree = statusText;

            gridView.Columns.Add(new GridViewColumn { Header = "Status", Width = 90, CellTemplate = statusTemplate });
            gridView.Columns.Add(new GridViewColumn { Header = "Item", Width = 150, CellTemplate = TextCell("Name", Text, FontWeights.SemiBold) });
            gridView.Columns.Add(new GridViewColumn { Header = "Reason", Width = 214, CellTemplate = TextCell("Reason", Muted, FontWeights.Normal) });
            gridView.Columns.Add(new GridViewColumn { Header = "Full path", Width = 318, CellTemplate = TextCell("FullPath", Text, FontWeights.Normal) });
            return list;
        }

        private static Style CreateGridHeaderStyle()
        {
            var style = new Style(typeof(GridViewColumnHeader));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Panel2));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Muted));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Border));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 7, 10, 7)));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12d));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            return style;
        }

        private static Style CreateResultItemStyle()
        {
            var style = new Style(typeof(System.Windows.Controls.ListViewItem));
            style.Setters.Add(new Setter(Control.MinHeightProperty, 32d));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 3, 6, 3)));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.ToolTipProperty, new Binding("FullPath")));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateResultItemTemplate()));
            return style;
        }

        private static ControlTemplate CreateResultItemTemplate()
        {
            var template = new ControlTemplate(typeof(System.Windows.Controls.ListViewItem));
            var border = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            border.Name = "RowChrome";
            border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(3));
            border.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(System.Windows.Controls.Border.SnapsToDevicePixelsProperty, true);
            border.SetBinding(System.Windows.Controls.Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(System.Windows.Controls.Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(System.Windows.Controls.Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });

            var presenter = new FrameworkElementFactory(typeof(GridViewRowPresenter));
            presenter.SetBinding(GridViewRowPresenter.ContentProperty, new Binding("Content") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetBinding(GridViewRowPresenter.ColumnsProperty, new Binding("View.Columns") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListView), 1) });
            presenter.SetBinding(GridViewRowPresenter.VerticalAlignmentProperty, new Binding("VerticalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
            border.AppendChild(presenter);
            template.VisualTree = border;

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(System.Windows.Controls.Border.BackgroundProperty, SurfaceRaised, "RowChrome"));
            template.Triggers.Add(hover);

            var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(System.Windows.Controls.Border.BackgroundProperty, Panel2, "RowChrome"));
            selected.Setters.Add(new Setter(System.Windows.Controls.Border.BorderBrushProperty, BorderStrong, "RowChrome"));
            template.Triggers.Add(selected);

            var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focus.Setters.Add(new Setter(System.Windows.Controls.Border.BorderBrushProperty, BlueLight, "RowChrome"));
            template.Triggers.Add(focus);

            return template;
        }

        private static DataTemplate TextCell(string propertyName, Brush brush, FontWeight weight)
        {
            var template = new DataTemplate();
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding(propertyName));
            text.SetBinding(FrameworkElement.ToolTipProperty, new Binding(propertyName));
            text.SetValue(TextBlock.ForegroundProperty, brush);
            text.SetValue(TextBlock.FontWeightProperty, weight);
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            text.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 8, 0));
            template.VisualTree = text;
            return template;
        }

        private UIElement BuildSettingsTab()
        {
            var group = Frame("Settings");
            var grid = new Grid { Margin = new Thickness(22, 16, 22, 16) };
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
            var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            grid.Children.Add(left);
            var middle = new StackPanel { Margin = new Thickness(16, 0, 16, 0) };
            Grid.SetColumn(middle, 1);
            grid.Children.Add(middle);
            var right = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);

            AddSectionHeading(left, "Scan behavior", "Tune what RED++ treats as reviewable during a scan.");
            ignoreEmptyFiles = SettingCheck(left, "Treat ignored files as removable trash", "Directories containing only ignored files can still be eligible.");
            ignoreSystem = SettingCheck(left, "Ignore system directories", "Recommended for routine cleanup and safer defaults.");
            ignoreHidden = SettingCheck(left, "Ignore hidden directories", "Skip hidden folders unless you explicitly want them reviewed.");
            respectGitIgnore = SettingCheck(left, "Respect .gitignore rules", "Project build outputs and ignored folders stay out of the review list.");
            deleteEmptyFiles = SettingCheck(left, "Include standalone zero-byte files", "Also review empty files that are not inside an empty directory.");

            AddSectionHeading(middle, "Display", "Keep result sets readable and focused.");
            hideScanErrors = SettingCheck(middle, "Hide scan errors in results", "Errors are still logged even when hidden from the tree.");
            hideIgnored = SettingCheck(middle, "Hide ignored directories", "Reduce noise when filters intentionally skip folders.");

            AddSectionHeading(middle, "Performance", "Keep deep trees responsive without changing scan safety.");
            fastRendering = SettingCheck(middle, "Fast result rendering", "Keeps the interface responsive on very large directory trees.");

            AddSectionHeading(right, "Safety", "Prefer reversible operations and clear recovery paths.");
            protectRoot = SettingCheck(right, "Protect the starting directory", "The selected root is never deleted even when it becomes empty.");
            hideDeletionErrors = SettingCheck(right, "Continue past deletion errors", "Leave failed items unchanged and continue with the remaining queue.");
            clipboardDetection = SettingCheck(right, "Detect folder paths in the clipboard", "Makes pasted Explorer paths easier to scan.");
            useMft = SettingCheck(right, "Use MFT turbo scan", "Administrator-only acceleration; standard scan is used when unavailable.");

            AddSectionHeading(right, "Deletion", "Choose the default action after review.");
            right.Children.Add(Label("Deletion mode", 15, Text, FontWeights.SemiBold, new Thickness(0, 0, 0, 8)));
            deleteMode = new WpfComboBox
            {
                Height = 42,
                Width = 310,
                FontSize = 15,
                Background = Panel,
                Foreground = Text,
                BorderBrush = BorderStrong,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 5, 8, 5)
            };
            deleteMode.FocusVisualStyle = FocusVisual;
            deleteMode.Resources.Add(typeof(ComboBoxItem), CreateComboBoxItemStyle());
            SetAutomation(deleteMode, "Deletion mode", "Choose whether RED++ simulates, recycles, deletes directly, or moves eligible results.");
            foreach (DeleteModes mode in DeleteModeItem.GetList())
            {
                deleteMode.Items.Add(new DeleteModeItem(mode));
            }
            right.Children.Add(deleteMode);
            return group;
        }

        private static Style CreateComboBoxItemStyle()
        {
            var style = new Style(typeof(ComboBoxItem));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Panel2));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 8, 12, 8)));
            var hover = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceRaised));
            style.Triggers.Add(hover);
            var selected = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, Blue));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, Text));
            style.Triggers.Add(selected);
            return style;
        }

        private void AddSectionHeading(StackPanel parent, string title, string helper)
        {
            if (parent.Children.Count > 0)
            {
                parent.Children.Add(new Border { Height = 1, Background = Border, Margin = new Thickness(0, 2, 0, 12) });
            }
            parent.Children.Add(Label(title, 17, Text, FontWeights.SemiBold, new Thickness(0, 0, 0, 4)));
            parent.Children.Add(Label(helper, 13, Muted, FontWeights.Normal, new Thickness(0, 0, 0, 10)));
        }

        private WpfCheckBox SettingCheck(StackPanel parent, string title, string helper)
        {
            var cb = new WpfCheckBox
            {
                Content = title,
                Foreground = Text,
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, string.IsNullOrWhiteSpace(helper) ? 11 : 3)
            };
            SetAutomation(cb, title, helper);
            parent.Children.Add(cb);
            if (!string.IsNullOrWhiteSpace(helper))
            {
                parent.Children.Add(new TextBlock
                {
                    Text = helper,
                    Foreground = Muted,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(28, 0, 0, 10)
                });
            }
            return cb;
        }

        private UIElement BuildFiltersTab()
        {
            var group = Frame("Filters");
            var grid = new Grid { Margin = new Thickness(22, 20, 22, 20) };
            var scroll = new ScrollViewer
            {
                Content = grid,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            SetFrameContent(group, scroll);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var helper = Label("These rules are applied during every scan. Use them to keep known-safe folders and ignored files out of destructive review.", 13, Muted, FontWeights.Normal, new Thickness(0, 0, 0, 16));
            helper.TextWrapping = TextWrapping.Wrap;
            Grid.SetColumnSpan(helper, 3);
            grid.Children.Add(helper);
            AddFilterList(grid, 0, "Directories: Ignore", config.Filters.DirectoriesToIgnore);
            AddFilterList(grid, 1, "Directories: Never Empty", config.Filters.DirectoriesNeverEmpty);
            AddFilterList(grid, 2, "Files: Ignore", config.Filters.FilesToIgnore);
            return group;
        }

        private void AddFilterList(Grid grid, int column, string title, List<string> rules)
        {
            var panel = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 16, 0, column == 2 ? 0 : 16, 0) };
            Grid.SetRow(panel, 1);
            Grid.SetColumn(panel, column);
            grid.Children.Add(panel);
            panel.Children.Add(Label(title, 16, Text, FontWeights.SemiBold, new Thickness(0, 0, 0, 4)));
            int count = rules == null ? 0 : rules.Count;
            panel.Children.Add(Label(CountLabel(count, "rule") + " active", 12, Muted2, FontWeights.Normal, new Thickness(0, 0, 0, 10)));
            var list = new ListBox
            {
                ItemsSource = rules,
                Background = Surface,
                Foreground = Text,
                BorderBrush = Border,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
                MinHeight = 340,
                FontSize = 14
            };
            list.FocusVisualStyle = FocusVisual;
            list.ItemContainerStyle = CreateListBoxItemStyle();
            SetAutomation(list, title, CountLabel(count, "filter rule") + " active.");
            panel.Children.Add(list);
        }

        private static Style CreateListBoxItemStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceRaised));
            style.Triggers.Add(hover);
            var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, Panel2));
            style.Triggers.Add(selected);
            return style;
        }

        private UIElement BuildAboutTab()
        {
            var group = Frame("About RED++");
            var stack = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };
            SetFrameContent(group, stack);
            // Environment.ProcessPath (apphost exe) is single-file safe; Assembly.Location is empty in a bundle.
            FileVersionInfo vi = FileVersionInfo.GetVersionInfo(Environment.ProcessPath);
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 22) };
            header.Children.Add(new WpfImage
            {
                Source = ToImageSource(Properties.Resources.x128_Project),
                Width = 64,
                Height = 64,
                Margin = new Thickness(0, 0, 18, 0)
            });
            var headerText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            headerText.Children.Add(Label("RED++", 30, Text, FontWeights.SemiBold, new Thickness(0, 0, 0, 4)));
            headerText.Children.Add(Label("Remove Empty Directories+ v" + vi.FileVersion, 17, Muted, FontWeights.Normal, new Thickness(0)));
            header.Children.Add(headerText);
            stack.Children.Add(header);

            stack.Children.Add(Label("A portable Windows utility for reviewing and removing empty directories with reversible defaults, filter controls, and headless automation.", 15, Muted, FontWeights.Normal, new Thickness(0, 0, 0, 22)));
            AddInfoLine(stack, IconShield, BlueLight, "Recovery-first cleanup", "Recycle Bin, move mode, dry run, and protected undo manifests help keep cleanup reversible.");
            AddInfoLine(stack, IconSearch, Green, "Built for large trees", "Virtualized review lists and optional MFT scan keep network and whole-volume scans responsive.");
            AddInfoLine(stack, IconLock, Amber, "Private by design", "No telemetry. Logs and crash reports stay local unless you choose to share them.");

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 0) };
            actions.Children.Add(OutlineButton("Project page", 158, 42, (s, e) => OpenUrl("https://github.com/SysAdminDoc/REDplusplus/"), IconInfo));
            var releases = OutlineButton("Releases", 134, 42, (s, e) => OpenUrl("https://github.com/SysAdminDoc/REDplusplus/releases"), IconCheck);
            releases.Margin = new Thickness(10, 0, 0, 0);
            actions.Children.Add(releases);
            var issues = OutlineButton("Report issue", 152, 42, (s, e) => OpenUrl("https://github.com/SysAdminDoc/REDplusplus/issues"), IconWarning);
            issues.Margin = new Thickness(10, 0, 0, 0);
            actions.Children.Add(issues);
            stack.Children.Add(actions);
            return group;
        }

        private void AddInfoLine(StackPanel parent, Geometry icon, Brush brush, string title, string body)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(IconPath(icon, brush, 22, 2.25, null, new Thickness(0, 2, 10, 0)));
            var text = new StackPanel();
            Grid.SetColumn(text, 1);
            text.Children.Add(Label(title, 15, Text, FontWeights.SemiBold, new Thickness(0, 0, 0, 2)));
            text.Children.Add(Label(body, 13, Muted, FontWeights.Normal, new Thickness(0)));
            row.Children.Add(text);
            parent.Children.Add(row);
        }

        private static void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        private void BuildCommandBar()
        {
            var bar = new Border
            {
                Background = Bg2,
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

            scanButton = ActionButton("Scan Folder", Blue, IconSearch, 168);
            SetAutomation(scanButton, "Scan folder", "Scan the selected folder for empty directories and empty files.");
            scanButton.Click += (s, e) => StartScan();
            primaryActions.Children.Add(scanButton);

            deleteButton = ActionButton("Review & Delete", Red, IconTrash, 214);
            SetAutomation(deleteButton, "Review and delete", "Review eligible results and confirm before changing anything.");
            deleteButton.Margin = new Thickness(12, 0, 0, 0);
            deleteButton.Click += (s, e) => StartDelete();
            primaryActions.Children.Add(deleteButton);

            cancelButton = ActionButton("Cancel", Panel2, IconCancel, 136);
            SetAutomation(cancelButton, "Cancel current operation", "Cancel the scan or deletion currently in progress.");
            cancelButton.Margin = new Thickness(12, 0, 0, 0);
            cancelButton.Click += (s, e) => core?.CancelCurrentProcess();
            primaryActions.Children.Add(cancelButton);

            var secondaryActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(secondaryActions, 1);
            grid.Children.Add(secondaryActions);

            extrasButton = OutlineButton("More", 118, 54, null, IconInfo);
            SetAutomation(extrasButton, "More actions", "Open restore, import, log, and export options.");
            extrasButton.Margin = new Thickness(12, 0, 0, 0);
            extrasButton.Click += (s, e) => ShowExtrasMenu();
            secondaryActions.Children.Add(extrasButton);

            exitButton = OutlineButton("Close", 118, 54, null, IconClose);
            SetAutomation(exitButton, "Close RED++");
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
                Background = Surface,
                BorderBrush = Border,
                BorderThickness = new Thickness(0, 1, 0, 0)
            };
            Grid.SetRow(border, 4);
            rootGrid.Children.Add(border);

            var grid = new Grid { Margin = new Thickness(20, 0, 22, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            border.Child = grid;

            readyText = new TextBlock { Text = "Ready", Foreground = Text, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(readyText, 0);
            grid.Children.Add(readyText);
            itemCountText = new TextBlock { Text = "0 results", Foreground = Text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(itemCountText, 1);
            grid.Children.Add(itemCountText);
            detailStatusText = new TextBlock { Text = "Nothing to delete yet.", Foreground = Muted, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(detailStatusText, 2);
            grid.Children.Add(detailStatusText);
            progressText = new TextBlock { Text = "", Foreground = Text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
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
                ToolTip = "Current operation progress",
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Hidden
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
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(0),
                SnapsToDevicePixels = true
            };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            outer.Child = grid;
            var header = new Border
            {
                Background = Panel2,
                BorderBrush = Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                Padding = new Thickness(18, 12, 18, 12)
            };
            var titleBlock = new TextBlock
            {
                Text = title,
                Foreground = Text,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            header.Child = titleBlock;
            var body = new Border
            {
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0)
            };
            Grid.SetRow(body, 1);
            grid.Children.Add(header);
            grid.Children.Add(body);
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
            return new TextBlock { Text = text, FontSize = size, Foreground = brush, FontWeight = weight, Margin = margin, TextWrapping = TextWrapping.Wrap };
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

        private WpfButton OutlineButton(string text, double width, double height, RoutedEventHandler click = null, Geometry icon = null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (icon != null)
            {
                row.Children.Add(IconPath(icon, Text, 18, 2.2, null, new Thickness(0, 0, 8, 0)));
            }
            row.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = Text,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });

            var button = new WpfButton
            {
                Content = row,
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
            hasScanned = false;
            itemCountText.Text = "0 results";
            RefreshResultsVisibility();
            runtimeWatch.Restart();
            runData.AddLogSpacer();
            runData.AddLogMessage("Scanning for empty directories...");
            UpdateUiState(true);
            detailStatusText.Text = "Scanning for empty directories...";
            progressBar.IsIndeterminate = true;
            progressText.Text = "";
            core.SearchingForEmptyDirectories();
        }

        private bool TryGetSelectedDirectory(out DirectoryInfo selectedDirectory)
        {
            selectedDirectory = null;
            string rawPath = pathBox == null ? string.Empty : pathBox.Text;
            rawPath = string.IsNullOrWhiteSpace(rawPath) ? string.Empty : rawPath.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                detailStatusText.Text = "Choose an existing folder before scanning.";
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
                detailStatusText.Text = "The folder path is not valid.";
                WpfMessageBox.Show(this, "That folder path is not valid.\n\n" + ex.Message, "RED++", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!selectedDirectory.Exists)
            {
                detailStatusText.Text = "The selected folder does not exist.";
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
            activeCore.OnProgressChanged += (s, e) => Dispatcher.BeginInvoke(() => detailStatusText.Text = Convert.ToString(e.UserState));
            activeCore.OnFoundEmptyDirectory += (s, e) => Dispatcher.BeginInvoke(() => AddOrUpdateResult(e.ScanResult));
            activeCore.OnFinishedScanForEmptyDirs += (s, e) => Dispatcher.BeginInvoke(() =>
            {
                runtimeWatch.Stop();
                if (config.Options.AutoProtectRoot && runData.StartFolder != null)
                {
                    activeCore.AddProtectedFolder(runData.StartFolder.FullName);
                }
                AddEmptyFileResults();
                int total = e.EmptyFolderCount + e.EmptyFileCount;
                string elapsed = FormatElapsed(runtimeWatch.Elapsed);
                detailStatusText.Text = total == 0
                    ? string.Format("Checked {0} in {1}. Nothing eligible.", CountLabel(e.FolderCount, "directory", "directories"), elapsed)
                    : string.Format("Found {0} and {1} in {2}. Review before deleting.",
                        CountLabel(e.EmptyFolderCount, "empty directory", "empty directories"),
                        CountLabel(e.EmptyFileCount, "empty file", "empty files"),
                        elapsed);
                itemCountText.Text = CountLabel(results.Count, "result");
                hasScanned = true;
                UpdateUiState(false);
                progressBar.IsIndeterminate = false;
                progressBar.Value = 0;
                progressText.Text = "";
                RefreshResultsVisibility();
            });
            activeCore.OnError += (s, e) => Dispatcher.BeginInvoke(() =>
            {
                UpdateUiState(false);
                progressBar.IsIndeterminate = false;
                progressText.Text = "";
                detailStatusText.Text = "Scan stopped after an error.";
                WpfMessageBox.Show(this, e.Message, "RED++ Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
            activeCore.OnCancelled += (s, e) => Dispatcher.BeginInvoke(() =>
            {
                UpdateUiState(false);
                progressBar.IsIndeterminate = false;
                progressText.Text = "";
                detailStatusText.Text = "Canceled.";
            });
            activeCore.OnAborted += (s, e) => Dispatcher.BeginInvoke(() =>
            {
                UpdateUiState(false);
                progressBar.IsIndeterminate = false;
                progressText.Text = "";
                detailStatusText.Text = "Stopped after an error.";
            });
            activeCore.OnDeleteProcessChanged += (s, e) => Dispatcher.BeginInvoke(() =>
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
            activeCore.OnDeleteError += (s, e) => Dispatcher.BeginInvoke(() =>
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
            activeCore.OnDeleteProcessFinished += (s, e) => Dispatcher.BeginInvoke(() =>
            {
                UpdateUiState(false);
                deleteButton.IsEnabled = false;
                detailStatusText.Text = BuildCompletionMessage(e.DeletedFolderCount, e.DeletedFileCount);
                progressBar.IsIndeterminate = false;
                progressBar.Value = 0;
                progressText.Text = "";
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
            itemCountText.Text = CountLabel(results.Count, "result");
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

        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalSeconds < 1d)
            {
                return "under 1 second";
            }
            if (elapsed.TotalSeconds < 60d)
            {
                int seconds = Math.Max(1, (int)Math.Round(elapsed.TotalSeconds));
                return CountLabel(seconds, "second");
            }
            return elapsed.ToString(@"m\:ss");
        }

        private static string CountLabel(int count, string singular, string plural = null)
        {
            return string.Format("{0} {1}", count, count == 1 ? singular : (plural ?? singular + "s"));
        }

        private void UpdateUiState(bool busy)
        {
            scanButton.IsEnabled = !busy;
            cancelButton.IsEnabled = busy;
            extrasButton.IsEnabled = !busy;
            readyText.Text = busy ? "Working" : "Ready";
            readyText.Foreground = busy ? Amber : Green;
            progressBar.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
            if (!busy)
            {
                progressText.Text = "";
            }
            // Screen readers should hear the state word, not the decorative bullet.
            System.Windows.Automation.AutomationProperties.SetName(readyText, busy ? "Working" : "Ready");
            if (deleteButton != null)
            {
                // Enable delete only when something is actually eligible — rows that
                // were merely kept (protected/never-empty) must not arm the button.
                deleteButton.IsEnabled = !busy && core != null && runData != null && EligibleResultCount() > 0;
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
            var container = new Grid { Margin = new Thickness(18) };
            container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            container.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            header.Children.Add(Label("Session Log", 18, Text, FontWeights.SemiBold, new Thickness(0, 0, 0, 4)));
            header.Children.Add(Label("The log records scan, delete, restore, and export activity for this session.", 13, Muted, FontWeights.Normal, new Thickness(0)));
            container.Children.Add(header);
            var logBox = new WpfTextBox
            {
                Text = log,
                IsReadOnly = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Foreground = Text,
                Background = Surface,
                BorderBrush = BorderStrong,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                TextWrapping = TextWrapping.NoWrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(logBox, 1);
            container.Children.Add(logBox);
            var win = new Window
            {
                Owner = this,
                Title = "RED++ Log",
                Width = 780,
                Height = 520,
                MinWidth = 560,
                MinHeight = 360,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Bg,
                FontFamily = UiFont,
                Content = container
            };
            var close = OutlineButton("Close", 112, 40, (s, e) => win.Close(), IconClose);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Margin = new Thickness(0, 14, 0, 0);
            Grid.SetRow(close, 2);
            container.Children.Add(close);
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
                Foreground = Text,
                Padding = new Thickness(4)
            };
            menu.Resources.Add(typeof(MenuItem), CreateMenuItemStyle());

            bool hasResults = results.Count > 0;

            menu.Items.Add(BuildRestoreMenu());
            menu.Items.Add(ExtrasMenuItem("Import dry-run results...", true, (s, e) => ImportDryRunResults()));
            menu.Items.Add(new Separator());
            menu.Items.Add(ExtrasMenuItem("View log", true, (s, e) => ShowLog()));
            menu.Items.Add(new Separator());
            menu.Items.Add(ExtrasMenuItem("Export results to file...", hasResults, (s, e) => ExportResultsToFile()));
            menu.Items.Add(ExtrasMenuItem("Copy results to clipboard", hasResults, (s, e) => ExportResultsToClipboard()));
            menu.IsOpen = true;
        }

        private static Style CreateMenuItemStyle()
        {
            var style = new Style(typeof(MenuItem));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Panel2));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 8, 18, 8)));
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5));
            style.Triggers.Add(disabled);
            return style;
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
                Header = "Restore deleted items",
                IsEnabled = true,
                Foreground = Text,
                Background = Panel2,
                Padding = new Thickness(14, 8, 18, 8)
            };
            SetAutomation(parent, "Restore deleted items", "Restore directories and empty files from a previous deletion run.");

            if (manifests.Count == 0)
            {
                parent.Items.Add(new MenuItem
                {
                    Header = "No restore points found",
                    IsEnabled = false,
                    Foreground = Muted,
                    Background = Panel2,
                    Padding = new Thickness(14, 8, 18, 8)
                });
                return parent;
            }

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
                        msg => Dispatcher.BeginInvoke(() => detailStatusText.Text = msg));
                }
                catch (Exception ex) { error = ex; }

                Dispatcher.BeginInvoke(() =>
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
            hasScanned = true;
            itemCountText.Text = CountLabel(results.Count, "result");
            if (deleteButton != null)
            {
                deleteButton.IsEnabled = false;
            }
            detailStatusText.Text = string.Format(
                "Imported {0} from {1}. {2} eligible. Re-scan the folder to enable deletion.",
                CountLabel(imported.ReviewCount, "record"),
                Path.GetFileName(fileName),
                eligible);
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
            var brush = new SolidColorBrush(ColorFrom(hex));
            brush.Freeze();
            return brush;
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
