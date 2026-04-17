using HandheldCompanion.Managers;
using HandheldCompanion.Misc;
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

            DeleteCommand = new DelegateCommand(() =>
            {
                ManagerFactory.collectionManager.DeleteCollection(collection.Id);
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
