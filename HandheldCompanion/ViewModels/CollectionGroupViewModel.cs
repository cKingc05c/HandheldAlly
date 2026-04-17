using HandheldCompanion.Managers;
using HandheldCompanion.Misc;
using HandheldCompanion.Properties;
using HandheldCompanion.Views;
using iNKORE.UI.WPF.Modern.Controls;
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace HandheldCompanion.ViewModels
{
    public class CollectionGroupViewModel : BaseViewModel, IComparable<CollectionGroupViewModel>, IComparable
    {
        public GameCollection? Collection { get; }
        private readonly string _builtInName;

        public string Name => Collection?.Name ?? _builtInName;

        public bool IsBuiltIn => Collection is null || Collection.IsBuiltIn;
        public bool CanDelete => !IsBuiltIn;

        public ObservableCollection<ProfileViewModel> Profiles { get; } = [];

        public ICommand? DeleteCommand { get; }

        // For built-in groups (Favorites, Other)
        public CollectionGroupViewModel(string builtInName)
        {
            _builtInName = builtInName;
        }

        // For user-created collections
        public CollectionGroupViewModel(GameCollection collection)
        {
            Collection = collection;
            _builtInName = string.Empty;

            DeleteCommand = new DelegateCommand(async () =>
            {
                Dialog dialog = new Dialog(MainWindow.GetCurrent())
                {
                    Title = string.Format(Resources.ProfilesPage_AreYouSureDelete1, collection.Name),
                    Content = Resources.ProfilesPage_AreYouSureDelete2,
                    CloseButtonText = Resources.ProfilesPage_Cancel,
                    PrimaryButtonText = Resources.ProfilesPage_Delete
                };

                ContentDialogResult result = await dialog.ShowAsync();
                switch (result)
                {
                    case ContentDialogResult.None:
                        dialog.Hide();
                        break;
                    case ContentDialogResult.Primary:
                        ManagerFactory.collectionManager.DeleteCollection(collection.Id);
                        break;
                }
            });
        }

        public void RefreshName()
        {
            OnPropertyChanged(nameof(Name));
        }

        public override string ToString() => Name;

        // Favorites first, user collections alphabetically, Other last
        public int CompareTo(CollectionGroupViewModel? other)
        {
            if (other is null) return 1;
            return SortKey().CompareTo(other.SortKey());
        }

        public int CompareTo(object? obj) => CompareTo(obj as CollectionGroupViewModel);

        private string SortKey()
        {
            if (IsBuiltIn && Name == "Favorites") return "0";
            if (IsBuiltIn) return "2_" + Name;
            return "1_" + Name;
        }
    }
}
