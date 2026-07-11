using System;
using System.Collections.Generic;
using System.Linq;
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

        SelectByTag(FolderSortKeyBox, _settings.Sorting.FolderKey);
        SelectByTag(FolderSortDirBox, _settings.Sorting.FolderDir);
        SelectByTag(FileSortKeyBox, _settings.Sorting.FileKey);
        SelectByTag(FileSortDirBox, _settings.Sorting.FileDir);

        ShowLineNumbers.IsChecked = _settings.Reading.ShowLineNumbers;
        HighlightCustomTags.IsChecked = _settings.Reading.HighlightCustomTags;

        SelectByTag(BodyStyleBox, _settings.Reading.BodyStyle);
        SelectByTag(TypefaceBox, _settings.Reading.Typeface);
        FontSizeBox.Value = _settings.Reading.FontSize;
        MarginsSlider.Value = _settings.Reading.MarginPct;
        MarginsReadout.Text = _settings.Reading.MarginPct + "%";

        SelectByTag(CollapseBelowBox, _settings.Outline.CollapseBelow.ToString());
        CollapseContainingBox.Text = _settings.Outline.CollapseContaining;

        UseTabsToggle.IsChecked = _settings.Tabs.Enabled;
        SingleInstanceToggle.IsChecked = _settings.SingleInstance.Enabled;
        IncomingNewTabToggle.IsChecked = _settings.Tabs.OpenIncomingInNewTab;

        CheckUpdatesToggle.IsChecked = _settings.Updates.CheckForUpdates;

        SearchMaxSizeBox.Value = Math.Round(_settings.Search.MaxFileBytes / (1024.0 * 1024.0), 2);
        SearchIncludeExtBox.Text = string.Join(", ", _settings.Search.IncludeExtensions);
        SearchExcludeExtBox.Text = string.Join(", ", _settings.Search.ExcludeExtensions);
        SearchExcludeFoldersBox.Text = string.Join(", ", _settings.Search.ExcludeFolders);
        SearchScanAllToggle.IsChecked = _settings.Search.ScanAllText;
        SearchHiddenToggle.IsChecked = _settings.Search.IncludeHidden;

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

        if (FolderSortKeyBox.SelectedItem is ComboBoxItem fsk && fsk.Tag is string fskv)
            _settings.Sorting.FolderKey = fskv;
        if (FolderSortDirBox.SelectedItem is ComboBoxItem fsd && fsd.Tag is string fsdv)
            _settings.Sorting.FolderDir = fsdv;
        if (FileSortKeyBox.SelectedItem is ComboBoxItem flk && flk.Tag is string flkv)
            _settings.Sorting.FileKey = flkv;
        if (FileSortDirBox.SelectedItem is ComboBoxItem fld && fld.Tag is string fldv)
            _settings.Sorting.FileDir = fldv;

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

        _settings.Updates.CheckForUpdates = CheckUpdatesToggle.IsChecked == true;

        _settings.Tabs.Enabled = UseTabsToggle.IsChecked == true;
        _settings.SingleInstance.Enabled = SingleInstanceToggle.IsChecked == true;
        _settings.Tabs.OpenIncomingInNewTab = IncomingNewTabToggle.IsChecked == true;

        if (SearchMaxSizeBox.Value is double mb && !double.IsNaN(mb))
            _settings.Search.MaxFileBytes = (long)(mb * 1024 * 1024);
        _settings.Search.IncludeExtensions = ParseCommaList(SearchIncludeExtBox.Text);
        _settings.Search.ExcludeExtensions = ParseCommaList(SearchExcludeExtBox.Text);
        _settings.Search.ExcludeFolders = ParseCommaList(SearchExcludeFoldersBox.Text);
        _settings.Search.ScanAllText = SearchScanAllToggle.IsChecked == true;
        _settings.Search.IncludeHidden = SearchHiddenToggle.IsChecked == true;
        _settings.Search.Normalize();   // clamp size/caps into valid ranges before save
    }

    private static List<string> ParseCommaList(string? text) =>
        (text ?? "")
            .Split(new[] { ',', ';', '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

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
