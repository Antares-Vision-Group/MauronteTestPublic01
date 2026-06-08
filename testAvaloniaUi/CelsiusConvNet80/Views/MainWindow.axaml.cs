using System;
using Avalonia.Controls;

namespace CelsiusConvNet80.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Handle window closing event
        // Handle window closed event
        if(DataContext is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception)
            {
                // Handle or log the exception
                e.Cancel = true; // Cancel the closing if necessary
            }
        }
    }

}