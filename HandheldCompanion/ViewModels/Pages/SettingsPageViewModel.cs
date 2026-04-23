using HandheldCompanion.Managers;
using HandheldCompanion.Misc;
using HandheldCompanion.ViewModels.Misc;
using HandheldCompanion.Views;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Data;
using static HandheldCompanion.Managers.UpdateManager;

namespace HandheldCompanion.ViewModels
{
    public class SettingsPageViewModel : BaseViewModel
    {
        // ── Update status ─────────────────────────────────────────────────

        private string _updateStatusText = Properties.Resources.SettingsPage_UpToDate;
        public string UpdateStatusText
        {
            get => _updateStatusText;
            private set { if (_updateStatusText != value) { _updateStatusText = value; OnPropertyChanged(nameof(UpdateStatusText)); } }
        }

        private string _updateDateText = Properties.Resources.SettingsPage_LastChecked;
        public string UpdateDateText
        {
            get => _updateDateText;
            private set { if (_updateDateText != value) { _updateDateText = value; OnPropertyChanged(nameof(UpdateDateText)); } }
        }

        private Visibility _updateDateVisibility = Visibility.Visible;
        public Visibility UpdateDateVisibility
        {
            get => _updateDateVisibility;
            private set { if (_updateDateVisibility != value) { _updateDateVisibility = value; OnPropertyChanged(nameof(UpdateDateVisibility)); } }
        }

        private Visibility _updateSymbolVisibility = Visibility.Collapsed;
        public Visibility UpdateSymbolVisibility
        {
            get => _updateSymbolVisibility;
            private set { if (_updateSymbolVisibility != value) { _updateSymbolVisibility = value; OnPropertyChanged(nameof(UpdateSymbolVisibility)); } }
        }

        private Visibility _progressBarVisibility = Visibility.Collapsed;
        public Visibility ProgressBarVisibility
        {
            get => _progressBarVisibility;
            private set { if (_progressBarVisibility != value) { _progressBarVisibility = value; OnPropertyChanged(nameof(ProgressBarVisibility)); } }
        }

        private Visibility _changelogVisibility = Visibility.Collapsed;
        public Visibility ChangelogVisibility
        {
            get => _changelogVisibility;
            private set { if (_changelogVisibility != value) { _changelogVisibility = value; OnPropertyChanged(nameof(ChangelogVisibility)); } }
        }

        private string _changelogText = string.Empty;
        public string ChangelogText
        {
            get => _changelogText;
            private set { if (_changelogText != value) { _changelogText = value; OnPropertyChanged(nameof(ChangelogText)); } }
        }

        private bool _checkUpdateEnabled = true;
        public bool CheckUpdateEnabled
        {
            get => _checkUpdateEnabled;
            private set { if (_checkUpdateEnabled != value) { _checkUpdateEnabled = value; OnPropertyChanged(nameof(CheckUpdateEnabled)); } }
        }

        // ── Update files ─────────────────────────────────────────────────

        public ObservableCollection<GithubUpdateViewModel> UpdateFiles { get; } = new();

        // ── Commands ─────────────────────────────────────────────────────

        public DelegateCommand CheckUpdateCommand { get; }

        // ── Constructor ─────────────────────────────────────────────────

        public SettingsPageViewModel()
        {
            BindingOperations.EnableCollectionSynchronization(UpdateFiles, _collectionLock);

            CheckUpdateCommand = new DelegateCommand(async () => await UpdateManager.StartProcess(false));

            UpdateManager.Updated += UpdateManager_Updated;

            // replay last known status
            UpdateDateText = Properties.Resources.SettingsPage_LastChecked + UpdateManager.GetTime();
        }

        // ── UpdateManager events ─────────────────────────────────────────

        private void UpdateManager_Updated(UpdateStatus status, UpdateFile? updateFile, object? value)
        {
            GithubUpdateViewModel? vm = updateFile is not null
                ? FindVm(updateFile)
                : null;

            switch (status)
            {
                case UpdateStatus.Initialized:
                case UpdateStatus.Updated:
                    UpdateStatusText = Properties.Resources.SettingsPage_UpToDate;
                    UpdateDateText = Properties.Resources.SettingsPage_LastChecked + UpdateManager.GetTime();
                    UpdateDateVisibility = Visibility.Visible;
                    UpdateSymbolVisibility = Visibility.Visible;
                    ProgressBarVisibility = Visibility.Collapsed;
                    CheckUpdateEnabled = true;
                    vm?.ResetToAvailable();
                    break;

                case UpdateStatus.Failed:
                    UpdateDateVisibility = Visibility.Visible;
                    UpdateSymbolVisibility = Visibility.Visible;
                    ProgressBarVisibility = Visibility.Collapsed;
                    CheckUpdateEnabled = true;
                    vm?.OnFailed();
                    break;

                case UpdateStatus.Checking:
                    lock (_collectionLock) { UpdateFiles.Clear(); }
                    UpdateStatusText = Properties.Resources.SettingsPage_UpdateCheck;
                    ChangelogVisibility = Visibility.Collapsed;
                    ChangelogText = string.Empty;
                    UpdateDateVisibility = Visibility.Collapsed;
                    UpdateSymbolVisibility = Visibility.Collapsed;
                    ProgressBarVisibility = Visibility.Visible;
                    CheckUpdateEnabled = false;
                    break;

                case UpdateStatus.Changelog:
                    ChangelogVisibility = Visibility.Visible;
                    UpdateDateVisibility = Visibility.Visible;
                    ChangelogText = value as string ?? string.Empty;
                    CheckUpdateEnabled = true;
                    break;

                case UpdateStatus.Ready:
                    ProgressBarVisibility = Visibility.Collapsed;
                    UpdateStatusText = Properties.Resources.SettingsPage_UpdateAvailable;
                    if (value is Dictionary<string, UpdateFile> files)
                    {
                        lock (_collectionLock)
                        {
                            UpdateFiles.Clear();
                            foreach (var file in files.Values)
                            {
                                var fileVm = new GithubUpdateViewModel(file);
                                fileVm.OnInstallFailed += OnInstallFailed;
                                UpdateFiles.Add(fileVm);
                            }
                        }
                    }
                    break;

                case UpdateStatus.Download:
                    vm?.OnDownloadStarted();
                    break;

                case UpdateStatus.Downloading:
                    if (value is int percent)
                        vm?.OnProgressChanged(percent);
                    break;

                case UpdateStatus.Downloaded:
                    vm?.OnDownloadCompleted();
                    CheckUpdateEnabled = true;
                    break;
            }
        }

        private GithubUpdateViewModel? FindVm(UpdateFile updateFile)
        {
            lock (_collectionLock)
            {
                foreach (var vm in UpdateFiles)
                    if (vm.UpdateFile == updateFile)
                        return vm;
            }
            return null;
        }

        private async void OnInstallFailed(GithubUpdateViewModel vm)
        {
            await new Dialog(MainWindow.GetCurrent())
            {
                Title = Properties.Resources.SettingsPage_UpdateWarning,
                Content = Properties.Resources.SettingsPage_UpdateFailedInstall,
                PrimaryButtonText = Properties.Resources.ProfilesPage_OK
            }.ShowAsync();
        }

        // ── Dispose ─────────────────────────────────────────────────────

        public override void Dispose()
        {
            UpdateManager.Updated -= UpdateManager_Updated;
            base.Dispose();
        }
    }
}
