// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AppleRemote.App.ViewModels;

namespace AppleRemote.App.Controls;

/// <summary>
/// Assignments browser: mouse click assigns to the highlighted remote control;
/// dragging a command onto a physical key picker assigns it to that control.
/// It does not hook or inject keyboard input.
/// </summary>
public partial class AssignmentPalette : UserControl
{
    Point _dragOrigin;
    AssignmentEntry? _dragCandidate;

    public AssignmentPalette() => InitializeComponent();

    void CommandItems_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragOrigin = e.GetPosition(CommandItems);
        _dragCandidate = FindEntry(e.OriginalSource as DependencyObject);
    }

    void CommandItems_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is null)
        {
            _dragCandidate = null;
            return;
        }
        Point now = e.GetPosition(CommandItems);
        if (Math.Abs(now.X - _dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(now.Y - _dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        AssignmentEntry entry = _dragCandidate;
        _dragCandidate = null;
        DragDrop.DoDragDrop(CommandItems,
            new DataObject(typeof(AssignmentEntry), entry), DragDropEffects.Copy);
    }

    static AssignmentEntry? FindEntry(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement fe && fe.DataContext is AssignmentEntry entry)
                return entry;
            source = source is Visual || source is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source) : null;
        }
        return null;
    }
}
