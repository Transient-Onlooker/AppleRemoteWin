// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 AnthoDingo

using System.Windows;
using System.Windows.Controls;

namespace AppleRemote.App.Views;

public partial class SettingsPage
{
    // The diagram is 690 DIP wide, plus 32 DIP of inter-column spacing.
    // Requiring 690+32+340+page margins on a 1024-pixel window hid the
    // settings pane behind the diagram. Stack sections below this threshold.
    const double TwoColumnMinWidth = 1160;
    bool _stackedLayout;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateResponsiveLayout(ActualWidth);
        SizeChanged += (_, e) => UpdateResponsiveLayout(e.NewSize.Width);
    }

    void UpdateResponsiveLayout(double availableWidth)
    {
        if (availableWidth <= 0 || double.IsNaN(availableWidth))
            return;
        bool stack = availableWidth < TwoColumnMinWidth;
        if (_stackedLayout == stack) return;
        _stackedLayout = stack;

        // Both controls remain alive with their original DataContext and
        // bindings. Only their layout slots change: no map/settings reset.
        Grid.SetColumn(RightSettingsScroller, stack ? 0 : 1);
        Grid.SetRow(RightSettingsScroller, stack ? 1 : 0);
        RemoteMappingPanel.Margin = stack
            ? new Thickness(0, 0, 0, 18)
            : new Thickness(0, 0, 32, 0);
    }
}
