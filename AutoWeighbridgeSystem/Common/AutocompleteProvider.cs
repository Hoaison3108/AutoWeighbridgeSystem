using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AutoWeighbridgeSystem.Common
{
    /// <summary>
    /// Lớp hỗ trợ đóng gói logic lọc (filter) cho ComboBox Autocomplete,
    /// giảm thiểu code lặp lại trong ViewModel.
    /// </summary>
    public partial class AutocompleteProvider<T> : ObservableObject
    {
        [ObservableProperty] private ObservableCollection<T> _items;
        [ObservableProperty] private string _filterText = "";
        
        public ICollectionView View { get; private set; }
        
        private readonly Func<T, string, bool> _filterPredicate;

        public AutocompleteProvider(IEnumerable<T> initialItems, Func<T, string, bool> filterPredicate)
        {
            _items = new ObservableCollection<T>(initialItems ?? Array.Empty<T>());
            _filterPredicate = filterPredicate ?? throw new ArgumentNullException(nameof(filterPredicate));
            
            View = CollectionViewSource.GetDefaultView(_items);
            View.Filter = OnFilter;
        }

        public void UpdateItems(IEnumerable<T> newItems)
        {
            Items = new ObservableCollection<T>(newItems ?? Array.Empty<T>());
            View = CollectionViewSource.GetDefaultView(Items);
            View.Filter = OnFilter;
            OnPropertyChanged(nameof(View));
        }

        public void ClearFilter()
        {
            FilterText = string.Empty;
        }

        partial void OnFilterTextChanged(string value)
        {
            View?.Refresh();
        }

        private bool OnFilter(object item)
        {
            if (string.IsNullOrWhiteSpace(FilterText)) return true;
            if (item is T typedItem)
            {
                return _filterPredicate(typedItem, FilterText);
            }
            return false;
        }
    }
}
