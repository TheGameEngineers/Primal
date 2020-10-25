// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrimalEditor
{
    /// <summary>
    /// Interaction logic for EnginePathDialog.xaml
    /// </summary>
    public partial class EnginePathDialog : Window
    {
        public string PrimalPath { get; private set; }
        public EnginePathDialog()
        {
            InitializeComponent();
            Owner = Application.Current.MainWindow;
        }

        private void OnOk_Button_Click(object sender, RoutedEventArgs e)
        {
            var path = pathTextBox.Text.Trim();
            messageTextBlock.Text = string.Empty;
            if(string.IsNullOrEmpty(path))
            {
                messageTextBlock.Text = "Invalid path.";
            }
            else if(path.IndexOfAny(Path.GetInvalidPathChars()) != -1)
            {
                messageTextBlock.Text = "Invalid character(s) used in path.";
            }
            else if(!Directory.Exists(Path.Combine(path, @"Engine\EngineAPI\")))
            {
                messageTextBlock.Text = "Unable to find the engine at the specified location.";
            }

            if(string.IsNullOrEmpty(messageTextBlock.Text))
            {
                if (!Path.EndsInDirectorySeparator(path)) path += @"\";
                PrimalPath = path;
                DialogResult = true;
                Close();
            }
        }
    }
}
