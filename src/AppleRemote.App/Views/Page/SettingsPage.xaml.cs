// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 AnthoDingo

using System.Windows;
using System.Windows.Controls;

namespace AppleRemote.App.Views;

public partial class SettingsPage
{
    // Key diagram requires 690 DIP + 32 DIP gutter; the other column needs
    // a comfortable 340 DIP. Stack before the two-column view gets cramped.
    const double TwoColumnMinWidth = 1160;
    const double GridHorizontalMargins = 36; // 18 DIP on either side
    const double ScrollbarReserve = 18;      // scrollbar + rounding headroom
    bool? _stackedLayout;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateResponsiveLayout();
        SettingsViewport.SizeChanged += (_, _) => UpdateResponsiveLayout();
    }

    void UpdateResponsiveLayout()
    {
        // With HorizontalScrollBarVisibility=Auto, a WPF ScrollViewer measures
        // its child at infinite width, so Auto+Star Grid columns can grow to
        // ~1796 DIP even in a 1280-wide window. Use a finite viewport width
        // and NEVER offer horizontal scrolling as a workaround.
        double viewportWidth = SettingsViewport.ActualWidth;
        if (viewportWidth <= 0 || double.IsNaN(viewportWidth) ||
            double.IsInfinity(viewportWidth))
            return;

        // Explicit content width prevents the nested Auto/Grid/StackPanel
        // controls from growing beyond the visible ScrollViewer viewport.
        // Reserve space even if the vertical scrollbar appears later.
        ResponsiveSettingsGrid.Width =
            Math.Max(0, viewportWidth - GridHorizontalMargins - ScrollbarReserve);

        bool stack = viewportWidth < TwoColumnMinWidth;
        if (_stackedLayout == stack) return;
        _stackedLayout = stack;

        if (stack)
        {
            ResponsiveSettingsGrid.ColumnDefinitions[0].Width =
                new GridLength(1, GridUnitType.Star);
            ResponsiveSettingsGrid.ColumnDefinitions[1].Width =
                new GridLength(0);
            Grid.SetColumn(RightSettingsScroller, 0);
            Grid.SetRow(RightSettingsScroller, 1);
            RemoteMappingPanel.Margin = new Thickness(0, 0, 0, 18);
        }
        else
        {
            ResponsiveSettingsGrid.ColumnDefinitions[0].Width = GridLength.Auto;
            ResponsiveSettingsGrid.ColumnDefinitions[1].Width =
                new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(RightSettingsScroller, 1);
            Grid.SetRow(RightSettingsScroller, 0);
            RemoteMappingPanel.Margin = new Thickness(0, 0, 32, 0);
        }
    }
}
