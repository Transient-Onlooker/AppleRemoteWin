using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using AppleRemote.App.ViewModels;
using AppleRemote.App.Models;

namespace AppleRemote.App.Controls;

/// <summary>Sélecteur d'action : un bouton qui ouvre un menu Key (sous-menu des touches) / Key Combo / Software / Macro.</summary>
public sealed class KeyPicker : Button
{
    public static readonly DependencyProperty InputIdProperty =
        DependencyProperty.Register(nameof(InputId), typeof(string), typeof(KeyPicker),
            new PropertyMetadata("", (d, _) => ((KeyPicker)d).RefreshSelection()));
    public static readonly DependencyProperty SelectedInputProperty =
        DependencyProperty.Register(nameof(SelectedInput), typeof(string), typeof(KeyPicker),
            new PropertyMetadata("", (d, _) => ((KeyPicker)d).RefreshSelection()));
    public static readonly DependencyProperty SelectInputCommandProperty =
        DependencyProperty.Register(nameof(SelectInputCommand), typeof(ICommand), typeof(KeyPicker));

    public string InputId { get => (string)GetValue(InputIdProperty); set => SetValue(InputIdProperty, value); }
    public string SelectedInput { get => (string)GetValue(SelectedInputProperty); set => SetValue(SelectedInputProperty, value); }
    public ICommand? SelectInputCommand { get => (ICommand?)GetValue(SelectInputCommandProperty); set => SetValue(SelectInputCommandProperty, value); }

    void RefreshSelection()
    {
        if (!string.IsNullOrEmpty(InputId) && InputId == SelectedInput)
        {
            SetResourceReference(BorderBrushProperty, "AccentFillColorDefaultBrush");
            BorderThickness = new Thickness(2);
        }
        else
        {
            ClearValue(BorderBrushProperty);
            ClearValue(BorderThicknessProperty);
        }
    }

    public static readonly DependencyProperty KeyProperty = DependencyProperty.Register(nameof(Key), typeof(string), typeof(KeyPicker),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((KeyPicker)d)._label.Text = Display((string)e.NewValue)));
    public static readonly DependencyProperty ChoicesProperty = DependencyProperty.Register(nameof(Choices), typeof(IEnumerable<string>), typeof(KeyPicker));

    public static readonly DependencyProperty MacrosProperty = DependencyProperty.Register(nameof(Macros), typeof(IEnumerable<string>), typeof(KeyPicker));
    public IEnumerable<string> Macros { get => (IEnumerable<string>)GetValue(MacrosProperty); set => SetValue(MacrosProperty, value); }

    public static readonly DependencyProperty SoftwareProperty = DependencyProperty.Register(nameof(Software), typeof(IEnumerable<string>), typeof(KeyPicker));
    public IEnumerable<string> Software { get => (IEnumerable<string>)GetValue(SoftwareProperty); set => SetValue(SoftwareProperty, value); }

    static string Display(string key) =>
        key.StartsWith(SoftwareApps.Prefix) ? SoftwareApps.NameOf(key[1..])
        : key.StartsWith(KeyCombos.Prefix) ? KeyCombos.Display(key)
        : key.StartsWith(Macro.Prefix) ? key[Macro.Prefix.Length..] : key;

    public string Key { get => (string)GetValue(KeyProperty); set => SetValue(KeyProperty, value); }
    public IEnumerable<string> Choices { get => (IEnumerable<string>)GetValue(ChoicesProperty); set => SetValue(ChoicesProperty, value); }

    readonly TextBlock _label = new() { Text = Display(""), TextTrimming = TextTrimming.CharacterEllipsis };

    public KeyPicker()
    {
        SetResourceReference(StyleProperty, typeof(Button)); // implicit styles don't reach subclasses: reuse WPF-UI's button look
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Padding = new Thickness(11, 5, 11, 5);
        var chevron = new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
        };
        chevron.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } } };
        Grid.SetColumn(chevron, 1);
        grid.Children.Add(_label);
        grid.Children.Add(chevron);
        Content = grid;
        Click += (_, _) =>
        {
            if (SelectInputCommand?.CanExecute(InputId) == true)
                SelectInputCommand.Execute(InputId);
            else Open();
        };
        MouseRightButtonUp += (_, e) =>
        {
            if (SelectInputCommand is null) return;
            Open(); // alternative legacy menu
            e.Handled = true;
        };
        AllowDrop = true;
        DragOver += (_, e) =>
        {
            e.Effects = SelectInputCommand is not null &&
                e.Data.GetDataPresent(typeof(AssignmentEntry)) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (SelectInputCommand is null ||
                e.Data.GetData(typeof(AssignmentEntry)) is not AssignmentEntry entry ||
                !SelectInputCommand.CanExecute(InputId)) return;
            SelectInputCommand.Execute(InputId);
            Key = entry.Value;
            e.Handled = true;
        };
    }

    /// <summary>Ouvre l'éditeur ; une macro nouvellement enregistrée est affectée au bouton.</summary>
    void EditMacro(Macro? existing)
    {
        var dlg = new Views.MacroEditorWindow(existing, Software ?? [], Macros ?? []) { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
        if (existing is null && dlg.SavedName is { } name) Key = Macro.Prefix + name;
    }

    void Open()
    {
        var keys = new MenuItem { Header = Loc.T("Pick.Key") };
        foreach (var name in Choices ?? [])
        {
            var item = new MenuItem { Header = name, IsChecked = name == Key };
            item.Click += (_, _) => Key = name;
            keys.Items.Add(item);
        }
        var menu = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.Bottom, MinWidth = ActualWidth };
        // Familiar "press a key to assign it" workflow. Reuse the existing
        // foreground-only keyboard capture dialog; no permanent keyboard hook.
        var record = new MenuItem { Header = Loc.T("Pick.RecordKey") };
        record.Click += (_, _) =>
        {
            var dlg = new Views.KeyComboWindow(recordAssignment: true) { Owner = Window.GetWindow(this) };
            dlg.ShowDialog();
            if (CapturedKeyBinding.FromRecordedCombo(dlg.Combo) is { } binding)
                Key = binding; // updates the existing key/shortcut persistence path
        };
        menu.Items.Add(record);
        menu.Items.Add(new Separator());
        menu.Items.Add(keys);
        var combo = new MenuItem { Header = Loc.T("Pick.KeyCombo"), IsChecked = Key.StartsWith(KeyCombos.Prefix) };
        combo.Click += (_, _) =>
        {
            var dlg = new Views.KeyComboWindow { Owner = Window.GetWindow(this) };
            dlg.ShowDialog();
            if (dlg.Combo is { } c) Key = c; // fermée sans valider : on garde la sélection précédente
        };
        menu.Items.Add(combo);
        var soft = new MenuItem { Header = Loc.T("Pick.Software") };
        foreach (var path in Software ?? [])
        {
            var item = new MenuItem { Header = SoftwareApps.NameOf(path), IsChecked = Key == SoftwareApps.Prefix + path };
            item.Click += (_, _) => Key = SoftwareApps.Prefix + path;
            soft.Items.Add(item);
        }
        if (soft.Items.Count > 0) soft.Items.Add(new Separator());
        var browse = new MenuItem { Header = Loc.T("Pick.Browse") };
        browse.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "*.exe|*.exe" };
            if (dlg.ShowDialog() == true) Key = SoftwareApps.Prefix + dlg.FileName;
        };
        soft.Items.Add(browse);
        menu.Items.Add(soft);
        var macro = new MenuItem { Header = Loc.T("Pick.Macro") };
        foreach (var name in Macros ?? [])
        {
            var item = new MenuItem { Header = name, IsChecked = Key == Macro.Prefix + name };
            item.Click += (_, _) => Key = Macro.Prefix + name;
            macro.Items.Add(item);
        }
        if (macro.Items.Count > 0) macro.Items.Add(new Separator());
        var create = new MenuItem { Header = Loc.T("Pick.NewMacro") };
        create.Click += (_, _) => EditMacro(null);
        macro.Items.Add(create);
        if (Key.StartsWith(Macro.Prefix))
        {
            var edit = new MenuItem { Header = Loc.T("Pick.EditMacro") };
            edit.Click += (_, _) => EditMacro(Macro.Load(Key[Macro.Prefix.Length..]));
            macro.Items.Add(edit);
            var dup = new MenuItem { Header = Loc.T("Pick.DuplicateMacro") };
            dup.Click += (_, _) =>
            {
                if (Macro.Duplicate(Key[Macro.Prefix.Length..]) is { } copy) Key = Macro.Prefix + copy;
            };
            macro.Items.Add(dup);
        }
        menu.Items.Add(macro);
        menu.IsOpen = true;
    }
}
