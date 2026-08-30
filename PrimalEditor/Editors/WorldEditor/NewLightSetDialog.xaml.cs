// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Components;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace PrimalEditor.Editors
{
    /// <summary>
    /// Interaction logic for NewLightSetDialog.xaml
    /// </summary>
    partial class NewLightSetDialog : Window
    {
        public string CurrentLightSet { get; init; }
        public string LightSetName { get; private set; }

        public NewLightSetDialog()
        {
            InitializeComponent();
        }

        private void OnNewLightSet_Dialog_Loaded(object sender, RoutedEventArgs e)
        {
            lightSetsListBox.SelectedItem = CurrentLightSet;
            LightSetName = CurrentLightSet;
            lightSetNameTextBox.Focus();
        }

        [GeneratedRegex(@"^[a-zA-Z0-9_ ]+$")]
        private static partial Regex ValidRegex();

        private bool CreateLightSet()
        {
            var name = lightSetNameTextBox.Text.Trim();
            // Validate the name. It should only contain alphanumeric characters, underscores or spaces, and should not be empty.
            if (string.IsNullOrEmpty(name) || !ValidRegex().IsMatch(name))
            {
                invalidNameTextBlock.Visibility = Visibility.Visible;
                return false;
            }
            invalidNameTextBlock.Visibility = Visibility.Collapsed;

            if (LightSet.LightSets.Contains(name))
            {
                warningTextBlock.Visibility = Visibility.Visible;
                return false;
            }
            else
            {
                warningTextBlock.Visibility = Visibility.Hidden;
                LightSet.AddLightSet(name, true);
            }

            lightSetsListBox.SelectedItem = name;
            return true;
        }

        private void OnCreate_Button_Click(object sender, RoutedEventArgs e)
        {
            CreateLightSet();
        }

        private void OnOk_Button_Click(object sender, RoutedEventArgs e)
        {
            LightSetName = lightSetsListBox.SelectedItem as string;
            DialogResult = !string.IsNullOrEmpty(LightSetName);
        }

        private void OnLightSet_TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && CreateLightSet())
            {
                OnOk_Button_Click(null, null);
                Close();
            }
        }
    }
}