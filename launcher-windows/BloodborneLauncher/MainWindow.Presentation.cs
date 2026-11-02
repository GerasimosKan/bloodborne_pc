using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace BloodborneLauncher;

public partial class MainWindow
{
    // Keep presentation events separate from configuration and launch contracts.
    void InitializePresentation()
    {
        var workArea = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, Math.Max(640, workArea.Width - 24));
        MinHeight = Math.Min(MinHeight, Math.Max(480, workArea.Height - 24));
        Width = Math.Min(Width, workArea.Width - 24);
        Height = Math.Min(Height, workArea.Height - 24);

        AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler(SettingsSelection_Changed));
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(SettingsToggle_Changed));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(SettingsToggle_Changed));
        SizeChanged += (_, _) =>
        {
            QuickSettingsGrid.Columns = ActualWidth < 1200 ? 3 : 5;
            SidebarArt.Height = ActualHeight < 800 ? 54 : 100;
        };
        StateChanged += (_, _) => MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    void SettingsSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!syncing && e.OriginalSource is ComboBox) FooterMessage.Text = "Unsaved changes";
    }

    void SettingsToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!syncing && e.OriginalSource is CheckBox) FooterMessage.Text = "Unsaved changes";
    }
}
