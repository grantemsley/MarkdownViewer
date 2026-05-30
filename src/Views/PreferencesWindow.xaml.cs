using System;
using System.Windows;
using System.Windows.Controls;
using MarkdownViewer.Models;
using MarkdownViewer.Services;
using WpfUiControls = Wpf.Ui.Controls;

namespace MarkdownViewer.Views;

public partial class PreferencesWindow : WpfUiControls.FluentWindow
{
    private readonly AppSettings _settings;
    // Start true so ValueChanged events fired during XAML parsing are ignored.
    // Cleared at the end of Load() once all controls are reachable.
    private bool _loading = true;
    private string _exePath = "";

    public PreferencesWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        Load();
    }

    private void Load()
    {
        SelectByTag(ThemeBox, _settings.Theme switch
        {
            "light" => "light",
            "dark" => "dark",
            _ => "system",
        });

        ShowExtensions.IsChecked = _settings.Files.ShowExtensions;
        ShowNonMarkdown.IsChecked = _settings.Files.ShowNonMarkdown;
        ShowHidden.IsChecked = _settings.Files.ShowHidden;
        WrapSidebar.IsChecked = _settings.Files.WrapSidebar;

        ShowLineNumbers.IsChecked = _settings.Reading.ShowLineNumbers;
        HighlightCustomTags.IsChecked = _settings.Reading.HighlightCustomTags;

        SelectByTag(BodyStyleBox, _settings.Reading.BodyStyle);
        SelectByTag(TypefaceBox, _settings.Reading.Typeface);
        FontSizeBox.Value = _settings.Reading.FontSize;
        MarginsSlider.Value = _settings.Reading.MarginPct;
        MarginsReadout.Text = _settings.Reading.MarginPct + "%";

        SelectByTag(CollapseBelowBox, _settings.Outline.CollapseBelow.ToString());
        CollapseContainingBox.Text = _settings.Outline.CollapseContaining;

        // Windows integration: reflect the current registry state. Setting
        // IsChecked raises Checked/Unchecked (which we don't handle), not Click,
        // so the toggle handlers below never fire from this assignment.
        _exePath = WindowsIntegrationService.ExePath;
        FileAssocToggle.IsChecked = WindowsIntegrationService.AreFileAssociationsRegistered(_exePath);
        ContextMenuToggle.IsChecked = WindowsIntegrationService.IsContextMenuRegistered(_exePath);

        _loading = false;
    }

    private static void SelectByTag(ComboBox cb, string tagValue)
    {
        foreach (var item in cb.Items)
            if (item is ComboBoxItem ci && Equals(ci.Tag?.ToString(), tagValue))
            { cb.SelectedItem = ci; return; }
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        Persist();
        DialogResult = true;
        Close();
    }

    private void Persist()
    {
        if (ThemeBox.SelectedItem is ComboBoxItem t && t.Tag is string tv)
            _settings.Theme = tv;

        _settings.Files.ShowExtensions = ShowExtensions.IsChecked == true;
        _settings.Files.ShowNonMarkdown = ShowNonMarkdown.IsChecked == true;
        _settings.Files.ShowHidden = ShowHidden.IsChecked == true;
        _settings.Files.WrapSidebar = WrapSidebar.IsChecked == true;

        _settings.Reading.ShowLineNumbers = ShowLineNumbers.IsChecked == true;
        _settings.Reading.HighlightCustomTags = HighlightCustomTags.IsChecked == true;
        if (BodyStyleBox.SelectedItem is ComboBoxItem bs && bs.Tag is string bsv)
            _settings.Reading.BodyStyle = bsv;
        if (TypefaceBox.SelectedItem is ComboBoxItem tf && tf.Tag is string tfv)
            _settings.Reading.Typeface = tfv;
        if (FontSizeBox.Value is double fs)
            _settings.Reading.FontSize = Math.Clamp((int)fs, 11, 22);
        _settings.Reading.MarginPct = Math.Clamp((int)MarginsSlider.Value, 50, 100);

        if (CollapseBelowBox.SelectedItem is ComboBoxItem cb && cb.Tag is string cbv &&
            int.TryParse(cbv, out var cbInt))
            _settings.Outline.CollapseBelow = cbInt;
        _settings.Outline.CollapseContaining = CollapseContainingBox.Text ?? "";
    }

    private void MarginsChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || MarginsReadout == null) return;
        MarginsReadout.Text = ((int)e.NewValue) + "%";
    }

    // Integration toggles apply immediately (they mutate the registry), unlike
    // the other prefs which persist on Done. Click fires only on user input.
    private void FileAssocToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try
        {
            if (FileAssocToggle.IsChecked == true)
                WindowsIntegrationService.RegisterFileAssociations(_exePath);
            else
                WindowsIntegrationService.UnregisterFileAssociations();
        }
        catch (Exception ex)
        {
            ShowIntegrationError("file associations", ex);
            FileAssocToggle.IsChecked = WindowsIntegrationService.AreFileAssociationsRegistered(_exePath);
        }
    }

    private void ContextMenuToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try
        {
            if (ContextMenuToggle.IsChecked == true)
                WindowsIntegrationService.RegisterContextMenu(_exePath);
            else
                WindowsIntegrationService.UnregisterContextMenu();
        }
        catch (Exception ex)
        {
            ShowIntegrationError("the context menu", ex);
            ContextMenuToggle.IsChecked = WindowsIntegrationService.IsContextMenuRegistered(_exePath);
        }
    }

    private void ShowIntegrationError(string what, Exception ex) =>
        MessageBox.Show(this, $"Couldn't update {what}:\n\n{ex.Message}",
            "MarkdownViewer", MessageBoxButton.OK, MessageBoxImage.Warning);
}
