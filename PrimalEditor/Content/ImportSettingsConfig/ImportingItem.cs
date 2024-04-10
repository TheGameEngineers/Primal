// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Utilities;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace PrimalEditor.Content
{
    enum ImportStatus
    {
        Importing,
        Succeeded,
        Failed
    }

    class ImportingItem : ViewModelBase
    {
        private DispatcherTimer _timer;
        private Stopwatch _stopwatch;

        private string _importDuration;
        public string ImportDuration
        {
            get => _importDuration;
            private set
            {
                if (_importDuration != value)
                {
                    _importDuration = value;
                    OnPropertyChanged(nameof(ImportDuration));
                }
            }
        }

        public string Name { get; }
        public Asset Asset { get; }
        private ImportStatus _status;
        public ImportStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        private double _progressMaximum;
        public double ProgressMaximum
        {
            get => _progressMaximum;
            private set
            {
                if (!_progressMaximum.IsTheSameAs(value))
                {
                    _progressMaximum = value;
                    OnPropertyChanged(nameof(ProgressMaximum));
                }
            }
        }

        private double _progressValue;
        public double ProgressValue
        {
            get => _progressValue;
            private set
            {
                if (!_progressValue.IsTheSameAs(value))
                {
                    _progressValue = value;
                    OnPropertyChanged(nameof(ProgressValue));
                }
            }
        }

        private double _normalizedValue;
        public double NormalizedValue
        {
            get => _normalizedValue;
            private set
            {
                if (!_normalizedValue.IsTheSameAs(value))
                {
                    _normalizedValue = value;
                    OnPropertyChanged(nameof(NormalizedValue));
                }
            }
        }

        public void SetProgress(int progress, int maxValue)
        {
            ProgressMaximum = maxValue;
            ProgressValue = progress;
            NormalizedValue = maxValue > 0 ? Math.Clamp(progress / maxValue, 0, 1) : 0.0;
        }

        private void UpdateTimer(object sender, EventArgs e)
        {
            if (Status == ImportStatus.Importing)
            {
                if (!_stopwatch.IsRunning) _stopwatch.Start();

                var t = _stopwatch.Elapsed;
                ImportDuration = string.Format("{0:00}:{1:00}:{2:00}", t.Minutes, t.Seconds, t.Milliseconds / 10);
            }
            else
            {
                _timer.Stop();
                _stopwatch.Stop();
            }
        }

        public ImportingItem(string name, Asset asset)
        {
            Debug.Assert(!string.IsNullOrEmpty(name) && asset != null);
            Asset = asset;
            Name = name;

            Application.Current.Dispatcher.Invoke(() =>
            {
                _stopwatch = new();
                _timer = new();
                _timer.Interval = TimeSpan.FromMilliseconds(100);
                _timer.Tick += UpdateTimer;
                _timer.Start();
            });
        }
    }

    static class ImportingItemCollection
    {
        private static ObservableCollection<ImportingItem> _importingItems;
        public static ReadOnlyObservableCollection<ImportingItem> ImportingItems { get; private set; }

        public static CollectionViewSource FilteredItems { get; private set; }

        private static readonly object _lockObject = new();
        private static AssetType _itemFilter = AssetType.Mesh;

        public static void SetItemFilter(AssetType assetType)
        {
            _itemFilter = assetType;
            FilteredItems.View.Refresh();
        }

        public static void Add(ImportingItem item)
        {
            lock (_lockObject) { Application.Current.Dispatcher.Invoke(() => _importingItems.Add(item)); }
        }

        public static void Remove(ImportingItem item)
        {
            lock (_lockObject) { Application.Current.Dispatcher.Invoke(() => _importingItems.Remove(item)); }
        }

        public static void Clear(AssetType assetType)
        {
            lock (_lockObject)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var item in _importingItems.Where(x => x.Asset.Type == assetType).ToList())
                    {
                        _importingItems.Remove(item);
                    }
                });
            }
        }

        public static ImportingItem GetItem(Asset asset)
        {
            lock (_lockObject) { return _importingItems.FirstOrDefault(x => x.Asset == asset); }
        }

        /// <summary>
        /// Calling this on a UI thread makes sure that all collections are created on the same thread.
        /// </summary>
        public static void Init() { }

        static ImportingItemCollection()
        {
            _importingItems = new();
            ImportingItems = new(_importingItems);
            FilteredItems = new() { Source = ImportingItems };
            FilteredItems.Filter += (s, e) =>
            {
                var type = (e.Item as ImportingItem).Asset.Type;
                e.Accepted = type == _itemFilter;
            };
        }
    }
}
