// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace PrimalEditor.Dictionaries;

public partial class ControlTemplates : ResourceDictionary
{
    private static void MoveUpFocus(UIElement element)
    {
        DependencyObject parent = element;
        while ((parent = VisualTreeHelper.GetParent(parent)) != null && Keyboard.Focus(parent as UIElement) == element) ;
    }

    private static void UpdateTextBoxSource(TextBox textBox, BindingExpression exp)
    {
        if (textBox.Tag is ICommand command && command.CanExecute(textBox.Text))
        {
            command.Execute(textBox.Text);
        }
        else
        {
            exp.UpdateSource();
        }
    }

    private void OnTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        var textBox = sender as TextBox;
        var exp = textBox.GetBindingExpression(TextBox.TextProperty);
        if (exp == null) return;

        if (e.Key is Key.Enter or Key.Tab)
        {
            UpdateTextBoxSource(textBox, exp);
            if (e.Key is Key.Enter)
            {
                MoveUpFocus(textBox);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            exp.UpdateTarget();
            MoveUpFocus(textBox);
        }
    }

    private void OnTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        var textBox = sender as TextBox;
        var exp = textBox.GetBindingExpression(TextBox.TextProperty);
        exp?.UpdateTarget();

        (sender as TextBox).SelectAll();
    }

    private void OnTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var textBox = sender as TextBox;
        if (!textBox.IsVisible) return;
        var exp = textBox.GetBindingExpression(TextBox.TextProperty);
        if(exp != null)
        {
            UpdateTextBoxSource(textBox, exp);
        }
    }

    private void OnTextBoxRename_KeyDown(object sender, KeyEventArgs e)
    {
        var textBox = sender as TextBox;
        var exp = textBox.GetBindingExpression(TextBox.TextProperty);
        if (exp == null) return;

        if (e.Key == Key.Enter)
        {
            UpdateTextBoxSource(textBox, exp);
            textBox.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            UpdateTextBoxSource(textBox, exp);
        }
        else if (e.Key == Key.Escape)
        {
            exp.UpdateTarget();
            textBox.Visibility = Visibility.Collapsed;
        }
    }

    private void OnTextBoxRename_LostFocus(object sender, RoutedEventArgs e)
    {
        var textBox = sender as TextBox;
        if (!textBox.IsVisible) return;
        var exp = textBox.GetBindingExpression(TextBox.TextProperty);
        if (exp != null)
        {
            exp.UpdateTarget();
            textBox.Visibility = Visibility.Collapsed;
        }
    }

    private void OnClose_Button_Click(object sender, RoutedEventArgs e)
    {
        var window = (Window)((FrameworkElement)sender).TemplatedParent;
        window.Close();
    }

    private void OnMaximizeRestore_Button_Click(object sender, RoutedEventArgs e)
    {
        var window = (Window)((FrameworkElement)sender).TemplatedParent;
        window.WindowState = (window.WindowState == WindowState.Normal) ?
            WindowState.Maximized : WindowState.Normal;
    }

    private void OnMinimize_Button_Click(object sender, RoutedEventArgs e)
    {
        var window = (Window)((FrameworkElement)sender).TemplatedParent;
        window.WindowState = WindowState.Minimized;
    }
}
