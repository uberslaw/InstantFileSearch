using System.Windows;
using System.Windows.Media;

namespace InstantFileSearch;

/// <summary>
/// Window-level Edit flag. When true, MainWindow.Resources overlay the rose
/// palette so DynamicResource chrome follows Edit without restyling each control.
/// Owned dialogs do not set this and stay on the application (View) brushes.
/// </summary>
public static class UiTheme
{
    public static readonly DependencyProperty IsEditModeProperty =
        DependencyProperty.RegisterAttached(
            "IsEditMode",
            typeof(bool),
            typeof(UiTheme),
            new PropertyMetadata(false, OnIsEditModeChanged));

    private static readonly (object SystemKey, string PaletteKey)[] SystemBrushMap =
    [
        (SystemColors.WindowBrushKey, UiThemePalette.BgBrush),
        (SystemColors.WindowTextBrushKey, UiThemePalette.TextBrush),
        (SystemColors.ControlBrushKey, UiThemePalette.PanelAltBrush),
        (SystemColors.ControlTextBrushKey, UiThemePalette.TextBrush),
        (SystemColors.ControlDarkBrushKey, UiThemePalette.BorderBrush),
        (SystemColors.GrayTextBrushKey, UiThemePalette.MutedBrush),
        (SystemColors.HighlightBrushKey, UiThemePalette.SelectionBrush),
        (SystemColors.HighlightTextBrushKey, UiThemePalette.SelectionTextBrush),
        (SystemColors.InactiveSelectionHighlightBrushKey, UiThemePalette.InactiveSelectionBrush),
        (SystemColors.InactiveSelectionHighlightTextBrushKey, UiThemePalette.TextBrush),
        (SystemColors.MenuBrushKey, UiThemePalette.PanelAltBrush),
        (SystemColors.MenuBarBrushKey, UiThemePalette.ToolbarBgBrush),
        (SystemColors.MenuTextBrushKey, UiThemePalette.TextBrush),
        (SystemColors.MenuHighlightBrushKey, UiThemePalette.SelectionBrush),
    ];

    public static bool GetIsEditMode(DependencyObject element) =>
        (bool)element.GetValue(IsEditModeProperty);

    public static void SetIsEditMode(DependencyObject element, bool value) =>
        element.SetValue(IsEditModeProperty, value);

    private static void OnIsEditModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            Apply(element.Resources, (bool)e.NewValue);
        }
    }

    public static void Apply(ResourceDictionary resources, bool isEditMode)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (!isEditMode)
        {
            foreach (var key in UiThemePalette.ResourceKeys)
            {
                RemoveKey(resources, key);
            }

            foreach (var (systemKey, _) in SystemBrushMap)
            {
                RemoveKey(resources, systemKey);
            }

            return;
        }

        var brushes = new Dictionary<string, SolidColorBrush>();
        foreach (var key in UiThemePalette.ResourceKeys)
        {
            var (r, g, b) = UiThemePalette.ParseRgb(UiThemePalette.Edit[key]);
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            brushes[key] = brush;
            resources[key] = brush;
        }

        foreach (var (systemKey, paletteKey) in SystemBrushMap)
        {
            resources[systemKey] = brushes[paletteKey];
        }
    }

    private static void RemoveKey(ResourceDictionary resources, object key)
    {
        if (resources.Contains(key))
        {
            resources.Remove(key);
        }
    }
}
