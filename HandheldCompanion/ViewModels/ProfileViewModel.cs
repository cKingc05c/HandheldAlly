using HandheldCompanion.Managers;
using HandheldCompanion.Misc;
using HandheldCompanion.Platforms;
using HandheldCompanion.Views;
using HandheldCompanion.Views.Windows;
using iNKORE.UI.WPF.Modern.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static HandheldCompanion.Managers.LibraryManager;

namespace HandheldCompanion.ViewModels
{
    public class ProfileViewModel : BaseViewModel
    {
        public ICommand? StartProcessCommand { get; private set; }
        public ICommand ToggleProcessCommand { get; private set; }
        public ICommand ToggleFavoriteCommand { get; private set; }
        public ICommand Navigate { get; private set; }
        public ICommand? OpenLayout { get; private set; }
        public ICommand? OpenExecutableLocation { get; private set; }

        public readonly bool IsQuickTools;
        public bool IsMainPage => !IsQuickTools;
        private readonly bool IsLibrary;
        private bool areVisualsVisible = true;
        private CancellationTokenSource? visualsLoadCancellationTokenSource;
        private ImageRequestKey? currentImageRequestKey;
        private bool visualsLoaded;
        private const int VisualUnloadDelayMs = 3000;
        private const int VisualReloadDelayMs = 150;
        private readonly HashSet<object> visibleVisualOwners = new(ReferenceEqualityComparer.Instance);
        private int visualsUnloadVersion;
        private int visualsReloadVersion;

        private readonly record struct ImageRequestKey(
            long Id,
            long CoverId,
            string CoverExtension,
            long ArtworkId,
            string ArtworkExtension,
            long LogoId,
            string LogoExtension);

        private Profile _Profile;
        public Profile Profile
        {
            get => _Profile;
            set
            {
                // Profile objects are mutable and re-assigned by reference after external mutation
                // (e.g. toggling IsLiked via gamepad). Reference equality would always be true in
                // that case, so we must always notify to keep bindings (IsLiked, templates, live
                // sort) and RefreshImages in sync.
                _Profile = value;

                // refresh all properties
                OnPropertyChanged(string.Empty);
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(CanOpenExecutableLocation));

                if (IsLibrary)
                    RefreshImages(forceReload: true);
                else
                    ReleaseVisuals(clearRequestKey: true);
            }
        }

        private void ApplyPlaceholderImages()
        {
            if (!ReferenceEquals(Cover, LibraryResources.MissingCover))
                Cover = LibraryResources.MissingCover;

            if (!ReferenceEquals(Artwork, LibraryResources.MissingArtwork))
                Artwork = LibraryResources.MissingArtwork;

            if (Logo is not null)
                Logo = null;

            visualsLoaded = false;
        }

        private static bool HasDisplayArtwork(BitmapImage? image)
        {
            if (image is null || image == LibraryResources.MissingArtwork)
                return false;

            return image.PixelWidth > 0 && image.PixelHeight > 0;
        }

        private static bool HasDisplayLogo(BitmapImage? image)
        {
            if (image is null)
                return false;

            return image.PixelWidth > 0 && image.PixelHeight > 0;
        }

        private BitmapImage? GetLaunchDialogArtwork()
        {
            if (HasDisplayArtwork(Artwork))
                return Artwork;

            if (_Profile.LibraryEntry is null)
                return null;

            BitmapImage? artwork = ManagerFactory.libraryManager.GetGameArt(
                _Profile.LibraryEntry.Id,
                LibraryType.artwork,
                _Profile.LibraryEntry.GetArtworkId(),
                _Profile.LibraryEntry.GetArtworkExtension(false));

            return HasDisplayArtwork(artwork) ? artwork : null;
        }

        private BitmapImage? GetLaunchDialogLogo()
        {
            if (HasDisplayLogo(Logo))
                return Logo;

            if (_Profile.LibraryEntry is null)
                return null;

            BitmapImage? logo = ManagerFactory.libraryManager.GetGameArt(
                _Profile.LibraryEntry.Id,
                LibraryType.logo,
                _Profile.LibraryEntry.GetLogoId(),
                _Profile.LibraryEntry.GetLogoExtension(false));

            return HasDisplayLogo(logo) ? logo : null;
        }

        private Dialog CreateLaunchDialog(Window owner)
        {
            BitmapImage? artwork = GetLaunchDialogArtwork();
            BitmapImage? logo = GetLaunchDialogLogo();

            if (IsQuickTools)
            {
                OverlayQuickTools quickTools = OverlayQuickTools.GetCurrent();
                ((OverlayQuickToolsViewModel)quickTools.DataContext).LaunchProfileDialog.Update(Name, Description, artwork, logo);
                return new Dialog(owner, quickTools.LaunchProfileContentDialog);
            }

            MainWindow mainWindow = MainWindow.GetCurrent();
            ((MainWindowViewModel)mainWindow.DataContext).LaunchProfileDialog.Update(Name, Description, artwork, logo);
            return new Dialog(owner, mainWindow.LaunchProfileContentDialog);
        }

        private void RefreshImages(bool forceReload = false)
        {
            if (!IsLibrary)
            {
                ReleaseVisuals(clearRequestKey: true);
                return;
            }

            if (IsLibrary && !areVisualsVisible)
            {
                ReleaseVisuals();
                return;
            }

            if (_Profile.LibraryEntry is null)
            {
                ReleaseVisuals(clearRequestKey: true);
                return;
            }

            // Library cards (deferVisualLoading) use the pre-downloaded thumbnail variants to
            // reduce both decode time and working-set memory compared to the full-resolution files.
            bool useThumbnails = IsLibrary;

            ImageRequestKey nextRequestKey = new(
                _Profile.LibraryEntry.Id,
                _Profile.LibraryEntry.GetCoverId(),
                _Profile.LibraryEntry.GetCoverExtension(useThumbnails),
                _Profile.LibraryEntry.GetArtworkId(),
                _Profile.LibraryEntry.GetArtworkExtension(useThumbnails),
                _Profile.LibraryEntry.GetLogoId(),
                _Profile.LibraryEntry.GetLogoExtension(useThumbnails));

            if (!forceReload && currentImageRequestKey == nextRequestKey && visualsLoaded)
                return;

            bool requestChanged = currentImageRequestKey != nextRequestKey;
            currentImageRequestKey = nextRequestKey;

            CancelPendingVisualLoad();
            CancelPendingVisualUnload();
            CancelPendingVisualReload();

            if (requestChanged)
                ApplyPlaceholderImages();

            CancellationTokenSource cancellationTokenSource = new();
            visualsLoadCancellationTokenSource = cancellationTokenSource;

            _ = LoadImagesAsync(nextRequestKey, cancellationTokenSource);
        }

        private async Task LoadImagesAsync(ImageRequestKey requestKey, CancellationTokenSource cancellationTokenSource)
        {
            try
            {
                CancellationToken cancellationToken = cancellationTokenSource.Token;

                BitmapImage? cover = await LoadImageAsync(
                    requestKey.Id,
                    IsLibrary ? LibraryType.cover | LibraryType.thumbnails : LibraryType.cover,
                    requestKey.CoverId,
                    requestKey.CoverExtension,
                    cancellationToken).ConfigureAwait(false);

                if (!await TryApplyLoadedImagesAsync(requestKey, cancellationTokenSource, cover: cover).ConfigureAwait(false))
                    return;

                BitmapImage? artwork = await LoadImageAsync(
                    requestKey.Id,
                    IsLibrary ? LibraryType.artwork | LibraryType.thumbnails : LibraryType.artwork,
                    requestKey.ArtworkId,
                    requestKey.ArtworkExtension,
                    cancellationToken).ConfigureAwait(false);

                if (!await TryApplyLoadedImagesAsync(requestKey, cancellationTokenSource, artwork: artwork).ConfigureAwait(false))
                    return;

                BitmapImage? logo = await LoadImageAsync(
                    requestKey.Id,
                    IsLibrary ? LibraryType.logo | LibraryType.thumbnails : LibraryType.logo,
                    requestKey.LogoId,
                    requestKey.LogoExtension,
                    cancellationToken).ConfigureAwait(false);

                await TryApplyLoadedImagesAsync(requestKey, cancellationTokenSource, logo: logo, markVisualsLoaded: true).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task<BitmapImage?> LoadImageAsync(long id, LibraryType libraryType, long imageId, string imageExtension, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return null;

                return ManagerFactory.libraryManager.GetGameArt(id, libraryType, imageId, imageExtension, cancellationToken);
            }, cancellationToken).ConfigureAwait(false);
        }

        private bool IsCurrentVisualLoad(ImageRequestKey requestKey, CancellationTokenSource cancellationTokenSource)
        {
            if (!ReferenceEquals(visualsLoadCancellationTokenSource, cancellationTokenSource) ||
                !areVisualsVisible ||
                currentImageRequestKey != requestKey)
                return false;

            try
            {
                return !cancellationTokenSource.IsCancellationRequested;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        private async Task<bool> TryApplyLoadedImagesAsync(
            ImageRequestKey requestKey,
            CancellationTokenSource cancellationTokenSource,
            BitmapImage? cover = null,
            BitmapImage? artwork = null,
            BitmapImage? logo = null,
            bool markVisualsLoaded = false)
        {
            if (!IsCurrentVisualLoad(requestKey, cancellationTokenSource))
                return false;

            bool applied = false;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!IsCurrentVisualLoad(requestKey, cancellationTokenSource))
                    return;

                if (cover is not null)
                    Cover = cover;

                if (artwork is not null)
                    Artwork = artwork;

                if (logo is not null || markVisualsLoaded)
                    Logo = logo;

                if (markVisualsLoaded)
                    visualsLoaded = true;

                applied = true;
            });

            return applied;
        }

        private void CancelPendingVisualLoad()
        {
            CancellationTokenSource? cancellationTokenSource = visualsLoadCancellationTokenSource;
            visualsLoadCancellationTokenSource = null;

            if (cancellationTokenSource is null)
                return;

            try
            {
                cancellationTokenSource.Cancel();
            }
            catch
            {
            }
            finally
            {
                cancellationTokenSource.Dispose();
            }
        }

        private void ReleaseVisuals(bool clearRequestKey = false)
        {
            CancelPendingVisualLoad();
            CancelPendingVisualUnload();
            CancelPendingVisualReload();

            if (clearRequestKey)
                currentImageRequestKey = null;

            ApplyPlaceholderImages();
        }

        public void SetVisualsVisible(object owner, bool isVisible, bool immediate = false)
        {
            if (!IsLibrary)
                return;

            if (isVisible)
                visibleVisualOwners.Add(owner);
            else
                visibleVisualOwners.Remove(owner);

            bool nextAreVisualsVisible = visibleVisualOwners.Count != 0;

            if (areVisualsVisible == nextAreVisualsVisible)
            {
                if (nextAreVisualsVisible)
                {
                    CancelPendingVisualUnload();
                    CancelPendingVisualReload();

                    if (!visualsLoaded)
                        RefreshImages();
                }

                return;
            }

            if (!isVisible && immediate)
            {
                areVisualsVisible = nextAreVisualsVisible;
                ReleaseVisuals();
                return;
            }

            areVisualsVisible = nextAreVisualsVisible;

            if (!nextAreVisualsVisible)
            {
                // Cancel any in-flight image load immediately to avoid wasted work.
                CancelPendingVisualLoad();
                CancelPendingVisualReload();

                if (!visualsLoaded)
                {
                    CancelPendingVisualUnload();
                    return;
                }

                // Delay the actual placeholder/memory clear so that normal scrolling
                // (cards passing briefly through the viewport edge) never triggers a
                // reload cycle.  Only cards that stay off-screen for the full delay
                // will have their images released.
                int unloadVersion = BeginVisualUnloadDelay();
                _ = DelayedUnloadAsync(unloadVersion);
            }
            else
            {
                // Card is back on screen — cancel any pending unload and reload.
                CancelPendingVisualUnload();
                int reloadVersion = BeginVisualReloadDelay();
                _ = DelayedReloadAsync(reloadVersion);
            }
        }

        private int BeginVisualUnloadDelay()
        {
            unchecked
            {
                return ++visualsUnloadVersion;
            }
        }

        private async Task DelayedUnloadAsync(int unloadVersion)
        {
            await Task.Delay(VisualUnloadDelayMs).ConfigureAwait(false);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (unloadVersion == visualsUnloadVersion && !areVisualsVisible)
                    ApplyPlaceholderImages();
            });
        }

        private int BeginVisualReloadDelay()
        {
            unchecked
            {
                return ++visualsReloadVersion;
            }
        }

        private async Task DelayedReloadAsync(int reloadVersion)
        {
            await Task.Delay(VisualReloadDelayMs).ConfigureAwait(false);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (reloadVersion == visualsReloadVersion && areVisualsVisible)
                    RefreshImages();
            });
        }

        private void CancelPendingVisualUnload()
        {
            unchecked { visualsUnloadVersion++; }
        }

        private void CancelPendingVisualReload()
        {
            unchecked { visualsReloadVersion++; }
        }

        public override string ToString()
        {
            return Name;
        }

        public string Name => _Profile.Name;
        public int SortOrder => _Profile.IsSubProfile ? 1 : 0;
        public string Description => _Profile.IsSubProfile ? _Profile.GetOwnerName() : _Profile.PlatformType.ToString();

        public DateTime DateCreated => _Profile.DateCreated;
        public DateTime DateModified => _Profile.DateModified;
        public DateTime LastUsed => _Profile.LastUsed;
        public bool IsLiked => _Profile.IsLiked;

        public IReadOnlyList<CollectionMenuItemViewModel> CollectionMenuItems
        {
            get
            {
                var items = new List<CollectionMenuItemViewModel>();

                // Favorites is always first
                items.Add(new CollectionMenuItemViewModel("Favorites", Profile.IsLiked,
                    new DelegateCommand(() =>
                    {
                        Profile.IsLiked = !Profile.IsLiked;
                        OnPropertyChanged(nameof(IsLiked));
                        OnPropertyChanged(nameof(CollectionMenuItems));
                        ManagerFactory.profileManager.UpdateOrCreateProfile(Profile);
                    })));

                // User collections
                foreach (GameCollection col in ManagerFactory.collectionManager.GetCollections())
                {
                    Guid colId = col.Id;
                    items.Add(new CollectionMenuItemViewModel(col.Name, Profile.Collections.Contains(colId),
                        new DelegateCommand(() =>
                        {
                            if (Profile.Collections.Contains(colId))
                                Profile.Collections.Remove(colId);
                            else
                                Profile.Collections.Add(colId);
                            OnPropertyChanged(nameof(CollectionMenuItems));
                            ManagerFactory.profileManager.UpdateOrCreateProfile(Profile);
                        })));
                }

                items.Add(CollectionMenuItemViewModel.Separator);
                items.Add(new CollectionMenuItemViewModel("New collection", false,
                    new AsyncDelegateCommand(async () =>
                    {
                        var textBox = new System.Windows.Controls.TextBox { MinWidth = 280 };
                        Window owner = IsQuickTools ? (Window)OverlayQuickTools.GetCurrent() : MainWindow.GetCurrent();
                        ContentDialogResult result = await new Dialog(owner)
                        {
                            Title = "New collection",
                            Content = textBox,
                            PrimaryButtonText = Properties.Resources.ProfilesPage_Yes,
                            CloseButtonText = Properties.Resources.ProfilesPage_Cancel,
                        }.ShowAsync();
                        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(textBox.Text))
                            return;
                        GameCollection newCol = ManagerFactory.collectionManager.CreateCollection(textBox.Text.Trim());
                        Profile.Collections.Add(newCol.Id);
                        OnPropertyChanged(nameof(CollectionMenuItems));
                        ManagerFactory.profileManager.UpdateOrCreateProfile(Profile);
                    }), isCheckable: false));

                return items;
            }
        }

        public GamePlatform PlatformType => _Profile.PlatformType;

        public bool IsRunning => ProcessManager.GetProcesses().Any(p => p.Path.Equals(Profile.Path));
        public bool IsAvailable => _Profile.CanExecute && !ProcessManager.GetProcesses().Any(p => p.Path.Equals(Profile.Path));
        public bool CanStopProcess => IsRunning;
        public bool CanToggleProcess => IsAvailable || CanStopProcess;
        public bool CanOpenExecutableLocation => !_Profile.ErrorCode.HasFlag(ProfileErrorCode.MissingPath);

        private bool _IsBusy;
        public bool IsBusy
        {
            get => _IsBusy;
            set
            {
                if (value != _IsBusy)
                {
                    _IsBusy = value;
                    OnPropertyChanged(nameof(IsBusy));
                }
            }
        }

        public ImageSource? Icon => _Profile.Icon;

        public Image? Platform
        {
            get
            {
                switch (PlatformType)
                {
                    default:
                    case GamePlatform.Generic:
                        return null;
                    case GamePlatform.Steam:
                        return PlatformManager.Steam?.GetLogo();
                    case GamePlatform.Origin:
                        return PlatformManager.Origin?.GetLogo();
                    case GamePlatform.EADesktop:
                        return PlatformManager.EADesktop?.GetLogo();
                    case GamePlatform.UbisoftConnect:
                        return PlatformManager.UbisoftConnect?.GetLogo();
                    case GamePlatform.GOG:
                        return PlatformManager.GOGGalaxy?.GetLogo();
                    case GamePlatform.BattleNet:
                        return PlatformManager.BattleNet?.GetLogo();
                    case GamePlatform.Epic:
                        return PlatformManager.Epic?.GetLogo();
                    case GamePlatform.RiotGames:
                        return PlatformManager.RiotGames?.GetLogo();
                    case GamePlatform.Rockstar:
                        return PlatformManager.Rockstar?.GetLogo();
                    case GamePlatform.MicrosoftStore:
                        return PlatformManager.MicrosoftStore?.GetLogo();
                }
            }
        }

        private BitmapImage? _cover;
        public BitmapImage? Cover
        {
            get => _cover;
            set
            {
                if (_cover != value)
                {
                    _cover = value;
                    OnPropertyChanged(nameof(Cover));
                }
            }
        }

        private BitmapImage? _artwork;
        public BitmapImage? Artwork
        {
            get => _artwork;
            set
            {
                if (_artwork != value)
                {
                    _artwork = value;
                    OnPropertyChanged(nameof(Artwork));
                }
            }
        }

        private BitmapImage? _logo;
        public BitmapImage? Logo
        {
            get => _logo;
            set
            {
                if (_logo != value)
                {
                    _logo = value;
                    OnPropertyChanged(nameof(Logo));
                }
            }
        }

        public ProfileViewModel(Profile profile, bool isQuickTools, bool isLibrary = false)
        {
            IsQuickTools = isQuickTools;
            IsLibrary = isLibrary;

            areVisualsVisible = !isLibrary;
            Profile = profile;

            ManagerFactory.processManager.ProcessStarted += ProcessManager_ProcessStarted;
            ManagerFactory.processManager.ProcessStopped += ProcessManager_ProcessStopped;

            StartProcessCommand = new DelegateCommand<bool>(async runAsAdmin =>
            {
                Window owner = isQuickTools ? OverlayQuickTools.GetCurrent() : MainWindow.GetCurrent();

                // localize me
                Dialog dialog = new Dialog(owner)
                {
                    Title = string.Empty,
                    Content = "The system cannot find the file specified.",
                    PrimaryButtonText = Properties.Resources.ProfilesPage_OK,
                    CanClose = true,
                };

                if (!File.Exists(profile.Path))
                {
                    ContentDialogResult result = await dialog.ShowAsync();
                    switch (result)
                    {
                        case ContentDialogResult.None:
                            dialog.Hide();
                            break;
                    }
                    return;
                }

                // localize me
                dialog = CreateLaunchDialog(owner);
                dialog.Title = string.Empty;
                dialog.PrimaryButtonText = string.Empty;
                dialog.SecondaryButtonText = string.Empty;
                dialog.CloseButtonText = string.Empty;
                dialog.CanClose = false;

                // display dialog
                _ = dialog.ShowAsync();

                // capture the UI context so background work can post back to hide the dialog
                var syncContext = SynchronizationContext.Current;

                try
                {
                    // set profile as favorite
                    ManagerFactory.profileManager.SetFavorite(Profile);

                    await Task.Run(async () =>
                    {
                        using (Process? process = profile.Launch(runAsAdmin))
                        {
                            // failed to start the process
                            if (process == null)
                                return;

                            // wait up to 60 sec for any visible window
                            List<string> execs = profile.GetExecutables(true);

                            Task timeout = Task.Delay(TimeSpan.FromSeconds(60));
                            while (!timeout.IsCompleted && !ProcessManager.GetProcesses().Any(p => execs.Contains(p.Path)))
                                await Task.Delay(300).ConfigureAwait(false);

                            if (ProcessManager.GetProcesses().Any(p => execs.Contains(p.Path)))
                                MainWindow.GetCurrent().SetState(WindowState.Minimized);

                            // hide the dialog
                            syncContext?.Post(_ => dialog.Hide(), null);

                            // Wait until none of the known executables are running
                            while (ProcessManager.GetProcesses().Any(p => execs.Contains(p.Path)))
                                await Task.Delay(1000).ConfigureAwait(false);

                            if (IsMainPage)
                                MainWindow.GetCurrent().SetState(WindowState.Normal);
                        }
                    }).ConfigureAwait(false);
                }
                catch { }
                finally
                {
                    // always hide the dialog
                    syncContext?.Post(_ => dialog.Hide(), null);
                }
            });

            ToggleProcessCommand = new DelegateCommand(async () =>
            {
                if (CanStopProcess)
                {
                    ProcessEx? processEx = ProcessManager.GetProcesses().FirstOrDefault(p => p.Path.Equals(Profile.Path));
                    if (processEx is not null)
                    {
                        ProcessExViewModel processViewModel = new(processEx, IsQuickTools);
                        try
                        {
                            processViewModel.KillProcessCommand?.Execute(null);
                        }
                        finally
                        {
                            // Don't dispose - let the async command complete
                            // processViewModel.Dispose();
                        }
                    }

                    return;
                }

                if (IsAvailable)
                    StartProcessCommand?.Execute(false);
            });

            ToggleFavoriteCommand = new DelegateCommand(() =>
            {
                Profile.IsLiked = !Profile.IsLiked;
                OnPropertyChanged(nameof(IsLiked));
                ManagerFactory.profileManager.UpdateOrCreateProfile(Profile);
            });

            Navigate = new DelegateCommand(async () =>
            {
                var page = MainWindow.profilesPage;

                // Set the selected main profile via ViewModel (MVVM)
                Profile target = Profile.IsSubProfile
                    ? ManagerFactory.profileManager.GetParent(Profile)
                    : Profile;

                // Use ViewModel instead of direct control access
                page.viewModel.SelectedMainProfile = target;

                // Set selected sub-profile
                if (Profile.IsSubProfile)
                    page.viewModel.SelectedProfile = Profile;

                MainWindow.GetCurrent().NavigateToPage("ProfilesPage");
            });

            OpenLayout = new DelegateCommand(() =>
            {
                var page = MainWindow.profilesPage;

                // Set the selected main profile via ViewModel (MVVM)
                Profile target = Profile.IsSubProfile
                    ? ManagerFactory.profileManager.GetParent(Profile)
                    : Profile;

                // Use ViewModel instead of direct control access
                page.viewModel.SelectedMainProfile = target;

                // Set selected sub-profile
                if (Profile.IsSubProfile)
                    page.viewModel.SelectedProfile = Profile;

                // prepare layout editor
                LayoutTemplate layoutTemplate = new(target.Layout)
                {
                    Name = target.LayoutTitle,
                    Description = Properties.Resources.ProfilesPage_Layout_Desc,
                    Author = Environment.UserName,
                    Executable = target.Executable,
                    Product = target.Name,
                };

                MainWindow.layoutPage.UpdateLayoutTemplate(layoutTemplate);
                MainWindow.NavView_Navigate(MainWindow.layoutPage);
            });

            OpenExecutableLocation = new DelegateCommand(() =>
            {
                if (!CanOpenExecutableLocation)
                    return;

                string? directory = Path.GetDirectoryName(Profile.Path);
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                    return;

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{Profile.Path}\"",
                    UseShellExecute = true,
                });
            });
        }

        private void ProcessManager_ProcessStarted(ProcessEx processEx, bool OnStartup)
        {
            ProcessManager_Changes(processEx.Path);
        }

        private void ProcessManager_ProcessStopped(ProcessEx processEx)
        {
            ProcessManager_Changes(processEx.Path);
        }

        public override void Dispose()
        {
            visibleVisualOwners.Clear();
            ReleaseVisuals(clearRequestKey: true);

            ManagerFactory.processManager.ProcessStarted -= ProcessManager_ProcessStarted;
            ManagerFactory.processManager.ProcessStopped -= ProcessManager_ProcessStopped;

            StartProcessCommand = null;
            OpenLayout = null;
            OpenExecutableLocation = null;

            base.Dispose();
        }

        private void ProcessManager_Changes(string path)
        {
            if (path.Equals(Profile.Path))
            {
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsAvailable));
                OnPropertyChanged(nameof(CanStopProcess));
                OnPropertyChanged(nameof(CanToggleProcess));
            }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new();

            public new bool Equals(object? x, object? y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
