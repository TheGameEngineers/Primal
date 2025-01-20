// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.GameProject;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PrimalEditor.Content
{
    class ContentSubfolderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var contentFolder = Project.Current.ContentPath;
            if(value is string folder && !string.IsNullOrEmpty(folder) && folder.Contains(contentFolder))
            {
                return $@"{Path.DirectorySeparatorChar}{folder.Replace(contentFolder, "")}";
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
    /// <summary>
    /// Interaction logic for ChangeDestinationFolder.xaml
    /// </summary>
    public partial class ChangeDestinationFolder : UserControl
    {
        public ChangeDestinationFolder()
        {
            InitializeComponent();
        }

        private void OnChangeDestinationFolder_Button_Click(object sender, RoutedEventArgs e)
        {
            var proxy = (sender as Button).DataContext as AssetProxy;
            var destinationFolder = proxy.DestinationFolder;
            if(Path.EndsInDirectorySeparator(destinationFolder))
            {
                destinationFolder = Path.GetDirectoryName(destinationFolder);
            }

            var dlg = new SelectFolderDialog(destinationFolder);

            if(dlg.ShowDialog() == true)
            {
                Debug.Assert(!string.IsNullOrEmpty(dlg.SelectedFolder));
                proxy.DestinationFolder = dlg.SelectedFolder;
            }
        }
    }
}
