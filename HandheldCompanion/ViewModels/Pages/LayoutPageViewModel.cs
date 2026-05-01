using HandheldCompanion.Controllers;
using HandheldCompanion.Managers;
using HandheldCompanion.Misc;
using HandheldCompanion.Views.Pages;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace HandheldCompanion.ViewModels
{
    public class LayoutPageViewModel : BaseViewModel
    {
        private ObservableCollection<LayoutTemplateViewModel> layoutList = [];
        public ListCollectionView LayoutCollectionView { get; set; }

        public LayoutPageViewModel(LayoutPage layoutPage)
        {
            // Enable thread-safe access to the collection
            BindingOperations.EnableCollectionSynchronization(layoutList, _collectionLock);

            // manage events
            ControllerManager.ControllerSelected += ControllerManager_ControllerSelected;

            LayoutCollectionView = new ListCollectionView(layoutList);
            LayoutCollectionView.GroupDescriptions.Add(new PropertyGroupDescription("Header"));

            // raise events
            switch (ManagerFactory.layoutManager.Status)
            {
                default:
                case ManagerStatus.Initializing:
                    ManagerFactory.layoutManager.Initialized += LayoutManager_Initialized;
                    break;
                case ManagerStatus.Initialized:
                    QueryLayouts();
                    break;
            }

            switch (ManagerFactory.settingsManager.Status)
            {
                default:
                case ManagerStatus.Initializing:
                    ManagerFactory.settingsManager.Initialized += SettingsManager_Initialized;
                    break;
                case ManagerStatus.Initialized:
                    QuerySettings();
                    break;
            }

            if (ControllerManager.HasTargetController && ControllerManager.GetTarget() is IController controller)
                ControllerManager_ControllerSelected(controller);
        }

        private void SettingsManager_Initialized()
        {
            QuerySettings();
        }

        private void QuerySettings()
        {
            ManagerFactory.settingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;
            RefreshLayoutList();
        }

        private void SettingsManager_SettingValueChanged(string? name, object? value, bool temporary)
        {
            switch (name)
            {
                case "LayoutFilterOnDevice":
                    RefreshLayoutList();
                    break;
            }
        }

        private void ControllerManager_ControllerSelected(IController? controller)
        {
            RefreshLayoutList();
        }

        private void LayoutManager_Initialized()
        {
            QueryLayouts();
            RefreshLayoutList();
        }

        private void QueryLayouts()
        {
            ManagerFactory.layoutManager.Updated += LayoutManager_Updated;
            foreach (LayoutTemplate template in LayoutManager.Templates)
                LayoutManager_Updated(template);
        }

        private void LayoutManager_Updated(LayoutTemplate layoutTemplate)
        {
            lock (_collectionLock)
            {
                LayoutTemplateViewModel? foundPreset = layoutList.FirstOrDefault(p => p.Guid == layoutTemplate.Guid);
                if (foundPreset is not null)
                {
                    int index = layoutList.IndexOf(foundPreset);
                    layoutList[index] = new(layoutTemplate);
                }
                else
                {
                    layoutList.Insert(0, new(layoutTemplate));
                }
            }

            RefreshLayoutList();
        }

        private void RefreshLayoutList()
        {
            // Get filter settings
            bool FilterOnDevice = ManagerFactory.settingsManager.GetBoolean("LayoutFilterOnDevice");

            // Get current controller
            IController? controller = ControllerManager.GetTarget();

            lock (_collectionLock)
            {
                foreach (LayoutTemplateViewModel layoutTemplate in layoutList)
                {
                    if (layoutTemplate.ControllerType is not null && FilterOnDevice)
                    {
                        if (layoutTemplate.ControllerType != controller?.GetType())
                        {
                            layoutTemplate.Visibility = Visibility.Collapsed;
                            continue;
                        }
                    }

                    layoutTemplate.Visibility = Visibility.Visible;
                }
            }
        }

        public override void Dispose()
        {
            base.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // manage events
                ManagerFactory.layoutManager.Updated -= LayoutManager_Updated;
                ManagerFactory.layoutManager.Initialized -= LayoutManager_Initialized;
                ManagerFactory.settingsManager.Initialized -= SettingsManager_Initialized;
                ManagerFactory.settingsManager.SettingValueChanged -= SettingsManager_SettingValueChanged;
                ControllerManager.ControllerSelected -= ControllerManager_ControllerSelected;
            }

            base.Dispose(disposing);
        }
    }
}
