// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Editors;
using PrimalEditor.GameProject;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace PrimalEditor.Content
{
    class DataSizeToStringConverter : IValueConverter
    {
        static readonly string[] _sizeSuffixes =
                   { "B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };

        static string SizeSuffix(long value, int decimalPlaces = 1)
        {
            if (value <= 0 || decimalPlaces < 0) return string.Empty;

            // mag is 0 for bytes, 1 for KB, 2, for MB, etc.
            int mag = (int)Math.Log(value, 1024);

            // 1L << (mag * 10) == 2 ^ (10 * mag) 
            // [i.e. the number of bytes in the unit corresponding to mag]
            decimal adjustedSize = (decimal)value / (1L << (mag * 10));

            // make adjustment when the value is large enough that
            // it would round up to 1000 or more
            if (Math.Round(adjustedSize, decimalPlaces) >= 1000)
            {
                mag += 1;
                adjustedSize /= 1024;
            }

            return string.Format("{0:n" + decimalPlaces + "} {1}", adjustedSize, _sizeSuffixes[mag]);
        }
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is long size) ? SizeSuffix(size, 0) : null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    class PlainView : ViewBase
    {
        public static readonly DependencyProperty ItemContainerStyleProperty =
          ItemsControl.ItemContainerStyleProperty.AddOwner(typeof(PlainView));

        public Style ItemContainerStyle
        {
            get { return (Style)GetValue(ItemContainerStyleProperty); }
            set { SetValue(ItemContainerStyleProperty, value); }
        }

        public static readonly DependencyProperty ItemTemplateProperty =
            ItemsControl.ItemTemplateProperty.AddOwner(typeof(PlainView));

        public DataTemplate ItemTemplate
        {
            get { return (DataTemplate)GetValue(ItemTemplateProperty); }
            set { SetValue(ItemTemplateProperty, value); }
        }

        public static readonly DependencyProperty ItemWidthProperty =
            WrapPanel.ItemWidthProperty.AddOwner(typeof(PlainView));

        public double ItemWidth
        {
            get { return (double)GetValue(ItemWidthProperty); }
            set { SetValue(ItemWidthProperty, value); }
        }

        public static readonly DependencyProperty ItemHeightProperty =
            WrapPanel.ItemHeightProperty.AddOwner(typeof(PlainView));

        public double ItemHeight
        {
            get { return (double)GetValue(ItemHeightProperty); }
            set { SetValue(ItemHeightProperty, value); }
        }

        protected override object DefaultStyleKey => new ComponentResourceKey(GetType(), "PlainViewResourceId");
    }

    /// <summary>
    /// Interaction logic for ContentBrowserView.xaml
    /// </summary>
    public partial class ContentBrowserView : UserControl, IDisposable
    {
        private string _sortedProperty = nameof(ContentInfo.FileName);
        private ListSortDirection _sortDirection;

        public SelectionMode SelectionMode
        {
            get => (SelectionMode)GetValue(SelectionModeProperty);
            set => SetValue(SelectionModeProperty, value);
        }

        public static readonly DependencyProperty SelectionModeProperty =
            DependencyProperty.Register(nameof(SelectionMode), typeof(SelectionMode), typeof(ContentBrowserView), new PropertyMetadata(SelectionMode.Extended));

        public FileAccess FileAccess
        {
            get => (FileAccess)GetValue(FileAccessProperty);
            set => SetValue(FileAccessProperty, value);
        }

        public static readonly DependencyProperty FileAccessProperty =
            DependencyProperty.Register(nameof(FileAccess), typeof(FileAccess), typeof(ContentBrowserView), new PropertyMetadata(FileAccess.ReadWrite));


        public bool AllowImport
        {
            get => (bool)GetValue(AllowImportProperty);
            set => SetValue(AllowImportProperty, value);
        }

        public static readonly DependencyProperty AllowImportProperty =
            DependencyProperty.Register(nameof(AllowImport), typeof(bool), typeof(ContentBrowserView), new PropertyMetadata(false));

        internal ContentInfo SelectedItem
        {
            get => (ContentInfo)GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(nameof(SelectedItem), typeof(ContentInfo), typeof(ContentBrowserView), new PropertyMetadata(null));

        public ContentBrowserView()
        {
            DataContext = null;
            InitializeComponent();
            Loaded += OnContentBrowserLoaded;
        }

        private void OnContentBrowserLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnContentBrowserLoaded;
            if (Application.Current?.MainWindow != null)
            {
                Application.Current.MainWindow.DataContextChanged += OnProjectChanged;
            }

            OnProjectChanged(null, new DependencyPropertyChangedEventArgs(DataContextProperty, null, Project.Current));
            folderListView.AddHandler(Thumb.DragDeltaEvent, new DragDeltaEventHandler(Thumb_DragDelta), true);
            folderListView.Items.SortDescriptions.Add(new SortDescription(_sortedProperty, _sortDirection));

            GeneratePathStackButtons();
        }

        private void Thumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (e.OriginalSource is Thumb thumb &&
                thumb.TemplatedParent is GridViewColumnHeader header)
            {
                if (header.Column.ActualWidth < 50)
                {
                    header.Column.Width = 50;
                }
                else if (header.Column.ActualWidth > 250)
                {
                    header.Column.Width = 250;
                }
            }
        }

        private void OnProjectChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            (DataContext as ContentBrowser)?.Dispose();
            DataContext = null;
            if (e.NewValue is Project project)
            {
                Debug.Assert(e.NewValue == Project.Current);
                var contentBrowser = new ContentBrowser(project);
                contentBrowser.PropertyChanged += OnSelectedFolderChanged;
                DataContext = contentBrowser;
            }
        }

        private void OnSelectedFolderChanged(object sender, PropertyChangedEventArgs e)
        {
            var vm = sender as ContentBrowser;
            if (e.PropertyName == nameof(vm.SelectedFolder) && !string.IsNullOrEmpty(vm.SelectedFolder))
            {
                GeneratePathStackButtons();
            }
        }

        private void GeneratePathStackButtons()
        {
            var vm = DataContext as ContentBrowser;
            var path = Directory.GetParent(Path.TrimEndingDirectorySeparator(vm.SelectedFolder)).FullName;
            var contentPath = Path.TrimEndingDirectorySeparator(vm.ContentFolder);

            pathStack.Children.RemoveRange(1, pathStack.Children.Count - 1);
            if (vm.SelectedFolder == vm.ContentFolder) goto _addCurrentDirectory;
            string[] paths = new string[3];
            string[] labels = new string[3];

            int i;
            for (i = 0; i < 3; ++i)
            {
                paths[i] = path;
                labels[i] = path[(path.LastIndexOf(Path.DirectorySeparatorChar) + 1)..];
                if (path == contentPath) break;
                path = path.Substring(0, path.LastIndexOf(Path.DirectorySeparatorChar));
            }

            if (i == 3) i = 2;
            for (; i >= 0; --i)
            {
                var btn = new Button()
                {
                    DataContext = paths[i],
                    Content = new TextBlock() { Text = labels[i], TextTrimming = TextTrimming.CharacterEllipsis }
                };
                pathStack.Children.Add(btn);
                if (i > 0) pathStack.Children.Add(new System.Windows.Shapes.Path());
            }

            pathStack.Children.Add(new System.Windows.Shapes.Path());

        _addCurrentDirectory:
            pathStack.Children.Add(new TextBlock()
            {
                Text = $"[ {Path.GetFileName(Path.TrimEndingDirectorySeparator(vm.SelectedFolder))} ]",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White,
                Margin = new(5, 0, 5, 0),
            });
        }

        private void OnPathStack_Button_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as ContentBrowser;
            vm.SelectedFolder = (sender as Button).DataContext as string;
        }

        private void OnGridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            var column = sender as GridViewColumnHeader;
            var sortBy = column.Tag.ToString();

            folderListView.Items.SortDescriptions.Clear();
            var newDir = ListSortDirection.Ascending;
            if (_sortedProperty == sortBy && _sortDirection == newDir)
            {
                newDir = ListSortDirection.Descending;
            }

            _sortDirection = newDir;
            _sortedProperty = sortBy;

            folderListView.Items.SortDescriptions.Add(new SortDescription(sortBy, newDir));
        }

        private void OnContent_Item_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var info = (sender as FrameworkElement).DataContext as ContentInfo;
            ExecuteSelection(info);
        }

        private void OnContent_Item_KeyDown(object sender, KeyEventArgs e)
        {
            var info = (sender as FrameworkElement).DataContext as ContentInfo;
            if (e.Key == Key.Enter)
            {
                ExecuteSelection(info);
            }
            else if (e.Key == Key.F2)
            {
                TryEdit(folderListView, info.FullPath);
            }
        }

        private void ExecuteSelection(ContentInfo info)
        {
            if (info == null) return;

            if (info.IsDirectory)
            {
                var vm = DataContext as ContentBrowser;
                vm.SelectedFolder = info.FullPath;
            }
            else if (FileAccess.HasFlag(FileAccess.Read))
            {
                var assetInfo = Asset.GetAssetInfo(info.FullPath);
                if (assetInfo != null)
                {
                    OpenAssetEditor(assetInfo);
                }
            }
        }

        private static IAssetEditor OpenAssetEditor(AssetInfo info)
        {
            IAssetEditor editor = null;
            try
            {
                switch (info.Type)
                {
                    case AssetType.Animation: break;
                    case AssetType.Audio: break;
                    case AssetType.Material: break;
                    case AssetType.Mesh:
                        editor = OpenEditorPanel<GeometryEditorView>(info, info.Guid, "Geometry Editor");
                        break;
                    case AssetType.Skeleton: break;
                    case AssetType.Texture:
                        editor = OpenEditorPanel<TextureEditorView>(info, info.Guid, "Texture Editor");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

            return editor;
        }

        private static IAssetEditor OpenEditorPanel<T>(AssetInfo info, Guid guid, string title)
            where T : FrameworkElement, new()
        {
            // First look for a window that's already open and is displaying the same asset.
            foreach (Window window in Application.Current.Windows)
            {
                if (window.Content is FrameworkElement content &&
                    content.DataContext is IAssetEditor editor &&
                    editor.AssetGuid == info.Guid)
                {
                    window.Activate();
                    return editor;
                }
            }

            // If not already open in an asset editor, we create a new window and load the asset.
            var newEditor = CreateEditorWindow<T>(title);
            (newEditor.DataContext as IAssetEditor).SetAsset(info);
            return newEditor.DataContext as IAssetEditor;
        }

        private static FrameworkElement CreateEditorWindow<T>(string title)
            where T : FrameworkElement, new()
        {
            var newEditor = new T();
            Debug.Assert(newEditor.DataContext is IAssetEditor);

            var win = new Window()
            {
                Content = newEditor,
                Title = title,
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Style = Application.Current.FindResource("PrimalWindowStyle") as Style
            };

            win.Show();
            return newEditor;
        }

        private void OnDropBorder_Drop(object sender, DragEventArgs e)
        {
            var vm = DataContext as ContentBrowser;
            if (Directory.Exists(vm.SelectedFolder) && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files?.Length > 0)
                {
                    if (e.OriginalSource == filesDrop)
                    {
                        new ConfigureImportSettings(files, vm.SelectedFolder).Import();
                        e.Handled = true;
                    }
                    else if (e.OriginalSource == cfgDrop)
                    {
                        OpenImportSettingsConfigurator(files, vm.SelectedFolder);
                        e.Handled = true;
                    }
                }
            }

            e.Effects = DragDropEffects.None;
            OnDropBorder_DragLeave(sender, e);
        }

        private static void OpenImportSettingsConfigurator(string[] files, string selectedFolder)
        {
            ConfigureImportSettings settingsConfigurator = null;
            // First, look for a window with this DataContext and add files to be configured for import.
            foreach (Window win in Application.Current.Windows)
            {
                if (win.DataContext is ConfigureImportSettings cfg)
                {
                    if(files?.Length>0)
                    {
                        cfg.AddFiles(files, selectedFolder);
                    }

                    settingsConfigurator = cfg;
                    win.Activate();
                    break;
                }
            }

            // If the window wasn't already open, create and show a new one.
            if (settingsConfigurator == null)
            {
                settingsConfigurator = (files?.Length > 0) ? new(files, selectedFolder) : new(selectedFolder);
                new ConfigureImportSettingsWindow()
                {
                    DataContext = settingsConfigurator,
                    Owner = Application.Current.MainWindow,
                }.Show();
            }
        }

        private void OnFolderContent_ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = folderListView.SelectedItem as ContentInfo;
            SelectedItem = item?.IsDirectory == true ? null : item;
        }

        private void TryEdit(ListBoxItem item)
        {
            var textBox = item.FindVisualChild<TextBox>();
            if (textBox != null)
            {
                textBox.Visibility = Visibility.Visible;
                textBox.Focus();
            }
        }

        private bool TryEdit(ListView list, string path)
        {
            foreach (ContentInfo item in list.Items)
            {
                if (item.FullPath == path)
                {
                    var listBoxItem = list.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
                    listBoxItem.IsSelected = true;
                    list.SelectedItem = item;
                    list.SelectedIndex = list.Items.IndexOf(item);
                    TryEdit(listBoxItem);
                    return true;
                }
            }

            return false;
        }

        private async void OnCreateNewFolder(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as ContentBrowser;
            var path = vm.SelectedFolder;
            if (!Path.EndsInDirectorySeparator(path)) path += Path.DirectorySeparatorChar;
            var folder = "NewFolder";
            var index = 1;
            while (Directory.Exists(path + folder))
            {
                folder = $"NewFolder{index++:0#}";
            }

            folder = path + folder;

            try
            {
                Directory.CreateDirectory(folder);
                var waitCounter = 0;
                // Wait up to 3 seconds for the OS to create the folder and
                // our file system watcher to make a new entry in Content Browser.
                while (waitCounter < 30 && !TryEdit(folderListView, folder))
                {
                    await Task.Run(() => Thread.Sleep(100));
                    ++waitCounter;
                }
            }
            catch (Exception ex)
            {

                Debug.WriteLine(ex.Message);
                Debug.WriteLine($"Error: failed to create new folder: {folder}");
            }
        }
        public void Dispose()
        {
            if (Application.Current?.MainWindow != null)
            {
                Application.Current.MainWindow.DataContextChanged -= OnProjectChanged;
            }

            (DataContext as ContentBrowser)?.Dispose();
            DataContext = null;
        }

        private void OnFolderContent_ListView_DragEnter(object sender, DragEventArgs e)
        {
            dropBorder.Opacity = 0;
            dropBorder.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(100)));
            dropBorder.BeginAnimation(OpacityProperty, fadeIn);
        }

        private void OnDropBorder_DragLeave(object sender, DragEventArgs e)
        {
            if (sender == dropBorder && e?.Effects != DragDropEffects.None)
            {
                var point = e.GetPosition(dropBorder);
                var result = VisualTreeHelper.HitTest(dropBorder, point);
                if (result != null)
                {
                    return;
                }
            }

            var fadeOut = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(100)));
            fadeOut.Completed += (_, _) => dropBorder.Visibility = Visibility.Collapsed;
            dropBorder.BeginAnimation(OpacityProperty, fadeOut);
        }
    }
}
