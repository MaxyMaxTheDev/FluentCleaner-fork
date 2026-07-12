using FluentCleaner.Services;
using FluentCleaner.ViewModels;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using System.IO;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FluentCleaner.Views;

public sealed partial class CleanerPage : Page, ISearchablePage, IPageActions
{
    public CleanerPageViewModel ViewModel { get; } = new();

    private bool _loaded;

    public CleanerPage()
    {
        InitializeComponent();
        // Auto-load on first appearance using whatever path settings resolves to
        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;

            AppSettings.Reload();
            var paths = AppSettings.Instance.ResolveDatabasePaths().ToList();
            if (paths.Count == 0) paths.Add(Path.Combine(AppContext.BaseDirectory, "Winapp2.ini"));
            await ViewModel.LoadWinapp2Async(paths);
        };
    }

    // OnNavigatedTo fires on every visit, even with NavigationCacheMode="Required".
    // Loaded only fires once — so this is the right place to pick up new custom entries.
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (_loaded)
            _ = ViewModel.RefreshCustomEntriesAsync();
    }

    // --- ISearchablePage ----------------------------------------------------------
    public void OnSearch(string text) => ViewModel.SearchText = text;

    // --- IPageActions ----------------------------------------------------------
    public void BuildActions(MenuFlyout flyout)
    {
        void Add(string label, Action action)
        {
            var item = new MenuFlyoutItem { Text = label };
            item.Click += (_, _) => action();
            flyout.Items.Add(item);
        }

        Add(ResourceService.Get("St_MenuSelectAll"),      () => ViewModel.SelectAllCommand.Execute(null));
        Add(ResourceService.Get("St_MenuSelectNone"),     () => ViewModel.SelectNoneCommand.Execute(null));
        Add(ResourceService.Get("St_MenuSelectDefaults"), () => ViewModel.SelectDefaultsCommand.Execute(null));
        flyout.Items.Add(new MenuFlyoutSeparator());
        Add(ResourceService.Get("St_MenuExpandAll"),      () => ViewModel.ExpandAllCommand.Execute(null));
        Add(ResourceService.Get("St_MenuCollapseAll"),    () => ViewModel.CollapseAllCommand.Execute(null));
        flyout.Items.Add(new MenuFlyoutSeparator());
        Add(ResourceService.Get("St_MenuSortDesc"),       () => ViewModel.SortResultsDescCommand.Execute(null));
        Add(ResourceService.Get("St_MenuSortAsc"),        () => ViewModel.SortResultsAscCommand.Execute(null));
        flyout.Items.Add(new MenuFlyoutSeparator());
        Add(ResourceService.Get("St_MenuRefresh"),        () => ViewModel.RefreshCommand.Execute(null));
    }

    // Open the detail view for the clicked result row
    private void ResultsListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ScanResultLine line && line.Result is not null)
            ViewModel.SelectedResultLine = line;
    }

    // Click a path row in the detail list;just highlight the file in Explorer.
    // Headers and registry keys are ignored; only real file paths get /select treatment.
    private void DetailList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not DetailLine { IsHeader: false } line) return;
        var path = line.Text;
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("HK", StringComparison.OrdinalIgnoreCase)) return;

        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    // Right-click "Exclude file";protects just this one file (FILE|dir|name)
    private void ExcludeFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string path } || string.IsNullOrWhiteSpace(path)) return;
        if (path.StartsWith("HK", StringComparison.OrdinalIgnoreCase)) return;

        var dir  = Path.GetDirectoryName(path);
        var file = Path.GetFileName(path);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file)) return;

        AddGlobalExclusion($"FILE|{dir}|{file}", ResourceService.Fmt("St_ExcludedFile", file));
    }

    // Right-click "Exclude folder";protects the entire parent folder tree (PATH|dir)
    private void ExcludeFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string path } || string.IsNullOrWhiteSpace(path)) return;
        if (path.StartsWith("HK", StringComparison.OrdinalIgnoreCase)) return;

        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir)) return;

        AddGlobalExclusion($"PATH|{dir}", ResourceService.Fmt("St_ExcludedFolder", dir));
    }

    private void AddGlobalExclusion(string rule, string status)
    {
        var settings = Services.AppSettings.Instance;
        if (settings.GlobalExclusions.Contains(rule, StringComparer.OrdinalIgnoreCase)) return;

        settings.GlobalExclusions.Add(rule);
        settings.GlobalExclusionsEnabled = true;
        settings.Save();
        ViewModel.StatusText = status;
    }

    // Entry flyout; Tag="{x:Bind}" gives us the CleanerEntryViewModel directly
    private async void EntryAnalyze_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: CleanerEntryViewModel vm })
            await ViewModel.AnalyzeSingleEntryAsync(vm);
    }

    private async void EntryClean_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: CleanerEntryViewModel vm })
        {
            if (!await CheckRunningBrowsersAsync([vm])) return;
            if (!await ConfirmWarningsAsync(ViewModel.GetWarningsForEntry(vm)))
                return;

            await ViewModel.CleanSingleEntryAsync(vm);
        }
    }

    // Ask Groq to explain the entry;result is cached so repeated opens are instant
    private async void EntryExplain_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: CleanerEntryViewModel vm }) return;

        var textBlock = new TextBlock
        {
            Text = ResourceService.Get("DlgExplainThinking"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400
        };

        var dialog = new ContentDialog
        {
            XamlRoot        = XamlRoot,
            RequestedTheme  = ActualTheme,
            CornerRadius    = new CornerRadius(8),
            Title           = vm.Name,
            CloseButtonText = ResourceService.Get("DlgExplainClose"),
            Content         = textBlock
        };

        // Show the dialog immediately (don't await), then fill in the answer
        var showTask = dialog.ShowAsync().AsTask();
        textBlock.Text = await AiExplainer.ExplainAsync(vm.Entry);
        await showTask;
    }


    // Category flyout;same trick with CleanerCategoryViewModel
    private async void CatAnalyze_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: CleanerCategoryViewModel vm })
            await ViewModel.AnalyzeCategoryAsync(vm);
    }

    private async void CatClean_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: CleanerCategoryViewModel vm })
        {
            var selected = vm.Entries.Where(e => e.IsSelected).ToList();
            if (!await CheckRunningBrowsersAsync(selected)) return;
            if (!await ConfirmWarningsAsync(ViewModel.GetWarningsForCategory(vm)))
                return;

            await ViewModel.CleanCategoryAsync(vm);
        }
    }

    private async void RunCleaner_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.RunCleanerCommand.CanExecute(null))
            return;

        if (!await CheckRunningBrowsersAsync()) return;
        if (!await ConfirmWarningsAsync(ViewModel.GetWarningsForSelectedEntries()))
            return;

        // Scan first so we can show what will be deleted
        await ((IAsyncRelayCommand)ViewModel.AnalyzeCommand).ExecuteAsync(null);

        if (ViewModel.LastScanResults.Count == 0)
        {
            ViewModel.StatusText = ResourceService.Get("St_NothingToClean");
            return;
        }

        if (!await ConfirmCleanAsync())
            return;

        await ((IAsyncRelayCommand)ViewModel.RunCleanerCommand).ExecuteAsync(null);
    }

    // Check for running browsers;only warns when browser entries are actually selected
    private static readonly (string Process, string DisplayName, int[] LangSecRefs)[] KnownBrowsers =
    [
        ("chrome",  "Google Chrome",    [3029]),
        ("firefox", "Mozilla Firefox",  [3026]),
        ("msedge",  "Microsoft Edge",   [3006]),
        ("opera",   "Opera",            [3027, 3035]),
        ("brave",   "Brave",            [3034]),
        ("vivaldi", "Vivaldi",          [3033]),
    ];

    private async Task<bool> CheckRunningBrowsersAsync(IEnumerable<CleanerEntryViewModel>? selectedEntries = null)
    {
        var selectedCodes = (selectedEntries ?? ViewModel.Categories.SelectMany(c => c.Entries).Where(e => e.IsSelected))
            .Select(e => e.Entry.LangSecRef ?? -1)
            .ToHashSet();

        var running = KnownBrowsers
            .Where(b => b.LangSecRefs.Any(selectedCodes.Contains))
            .Where(b => System.Diagnostics.Process.GetProcessesByName(b.Process).Length > 0)
            .Select(b => b.DisplayName)
            .ToList();

        if (running.Count == 0)
            return true;

        var dialog = new ContentDialog
        {
            XamlRoot          = XamlRoot,
            RequestedTheme    = ActualTheme,
            CornerRadius      = new CornerRadius(8),
            Title             = ResourceService.Get("DlgBrowsersTitle"),
            PrimaryButtonText = ResourceService.Get("DlgBrowsersContinue"),
            CloseButtonText   = ResourceService.Get("DlgBrowsersCancel"),
            DefaultButton     = ContentDialogButton.Close,
            Content           = ResourceService.Fmt("DlgBrowsersMessage", string.Join(", ", running))
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // Show a warning dialog if any of the selected entries have warnings;return true to proceed with cleaning
    private async Task<bool> ConfirmWarningsAsync(IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
            return true;

        var dialog = new ContentDialog
        {
            XamlRoot          = XamlRoot,
            RequestedTheme    = ActualTheme,
            CornerRadius      = new CornerRadius(8),
            Title             = ResourceService.Get("DlgWarningTitle"),
            PrimaryButtonText = ResourceService.Get("DlgWarningContinue"),
            CloseButtonText   = ResourceService.Get("DlgWarningCancel"),
            DefaultButton     = ContentDialogButton.Close,
            Content = new ScrollViewer
            {
                MaxHeight = 360,
                Content = new TextBlock
                {
                    Text =
                        ResourceService.Get("DlgWarningMessage") +
                        $"{Environment.NewLine}{Environment.NewLine}" +
                        string.Join($"{Environment.NewLine}{Environment.NewLine}", warnings),
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // Pre-clean confirmation dialog: shows every file and registry key that will be deleted
    private async Task<bool> ConfirmCleanAsync()
    {
        var results = ViewModel.LastScanResults;
        int totalFiles = results.Sum(r => r.FilesToDelete.Count);
        int totalReg   = results.Sum(r => r.RegistryToDelete.Count);
        long totalBytes = results.Sum(r => r.TotalBytes);

        var stack = new StackPanel { Spacing = 4 };

        // Header with totals
        stack.Children.Add(new TextBlock
        {
            Text       = ResourceService.Fmt("DlgCleanSummary", totalFiles, totalReg, ScanResult.FormatBytes(totalBytes)),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin     = new Thickness(0, 0, 0, 8)
        });

        // Per-entry groups
        bool isDark = ActualTheme == Microsoft.UI.Xaml.ElementTheme.Dark;
        foreach (var result in results.OrderByDescending(r => r.TotalBytes))
        {
            // Entry header
            var entryHeader = new TextBlock
            {
                Text       = $"{result.Entry.Name}  ({result.FormattedSize})",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    isDark ? Windows.UI.Color.FromArgb(255, 150, 200, 255)
                           : Windows.UI.Color.FromArgb(255, 30, 80, 160)),
                Margin     = new Thickness(0, 8, 0, 2)
            };
            stack.Children.Add(entryHeader);

            // Files (show first 10, then "... and N more")
            int fileLimit = 10;
            var files = result.FilesToDelete.Take(fileLimit).ToList();
            foreach (var file in files)
            {
                stack.Children.Add(new TextBlock
                {
                    Text        = $"  {file}",
                    FontSize    = 11,
                    Foreground  = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        isDark ? Windows.UI.Color.FromArgb(255, 200, 200, 200)
                               : Windows.UI.Color.FromArgb(255, 80, 80, 80)),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            if (result.FilesToDelete.Count > fileLimit)
            {
                stack.Children.Add(new TextBlock
                {
                    Text       = $"  ... {ResourceService.Fmt("DlgCleanAndMore", result.FilesToDelete.Count - fileLimit, ResourceService.Get("SuffixFiles"))}",
                    FontSize   = 11,
                    FontStyle  = Microsoft.UI.Xaml.FontStyle.Italic,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        isDark ? Windows.UI.Color.FromArgb(255, 150, 150, 150)
                               : Windows.UI.Color.FromArgb(255, 120, 120, 120))
                });
            }

            // Registry keys
            int regLimit = 5;
            var regs = result.RegistryToDelete.Take(regLimit).ToList();
            foreach (var reg in regs)
            {
                stack.Children.Add(new TextBlock
                {
                    Text        = $"  {reg}",
                    FontSize    = 11,
                    Foreground  = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        isDark ? Windows.UI.Color.FromArgb(255, 200, 200, 200)
                               : Windows.UI.Color.FromArgb(255, 80, 80, 80)),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            if (result.RegistryToDelete.Count > regLimit)
            {
                stack.Children.Add(new TextBlock
                {
                    Text       = $"  ... {ResourceService.Fmt("DlgCleanAndMore", result.RegistryToDelete.Count - regLimit, ResourceService.Get("SuffixRegistry"))}",
                    FontSize   = 11,
                    FontStyle  = Microsoft.UI.Xaml.FontStyle.Italic,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        isDark ? Windows.UI.Color.FromArgb(255, 150, 150, 150)
                               : Windows.UI.Color.FromArgb(255, 120, 120, 120))
                });
            }
        }

        var dialog = new ContentDialog
        {
            XamlRoot          = XamlRoot,
            RequestedTheme    = ActualTheme,
            CornerRadius      = new CornerRadius(8),
            Title             = ResourceService.Get("DlgCleanTitle"),
            PrimaryButtonText = ResourceService.Fmt("DlgCleanDelete", ScanResult.FormatBytes(totalBytes)),
            CloseButtonText   = ResourceService.Get("DlgCleanCancel"),
            DefaultButton     = ContentDialogButton.Primary,
            Content           = new ScrollViewer { MaxHeight = 480, Content = stack }
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // Show/hide the [...] button when hovering over a category header or entry row.
    // the buttons sit at Opacity="0" 
    private void CatHeader_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        SetMenuButtonOpacity(sender, 1);
    private void CatHeader_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        SetMenuButtonOpacity(sender, 0);
    private void EntryRow_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        SetMenuButtonOpacity(sender, 1);
    private void EntryRow_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        SetMenuButtonOpacity(sender, 0);

    private static void SetMenuButtonOpacity(object sender, double opacity)
    {
        if (sender is Grid g)
            foreach (var btn in g.Children.OfType<Button>())
                btn.Opacity = opacity;
    }
}
