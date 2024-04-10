// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.GameProject;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrimalEditor.Content
{
    /// <summary>
    /// Interaction logic for SelectFolderDialog.xaml
    /// </summary>
    public partial class SelectFolderDialog : Window
    {
        public string SelectedFolder { get; private set; }
        public SelectFolderDialog(string startFolder)
        {
            InitializeComponent();

            contentBrowserView.Loaded += (_, _) =>
            {
                // TODO: make sure that all paths always end with a directory separator character, application-wide!
                var startPath = startFolder + Path.DirectorySeparatorChar;

                if (startPath.Contains(Project.Current.ContentPath))
                {
                    (contentBrowserView.DataContext as ContentBrowser).SelectedFolder = startFolder;
                }
            };

            Closing += OnDialogClosing;
        }

        private void OnSelectFolder_Button_Click(object sender, RoutedEventArgs e)
        {
            var contentBrowser = contentBrowserView.DataContext as ContentBrowser;
            SelectedFolder = contentBrowser.SelectedFolder;
            DialogResult = true;
            Close();
        }

        private void OnDialogClosing(object sender, CancelEventArgs e)
        {
            contentBrowserView.Dispose();
        }
    }
}
