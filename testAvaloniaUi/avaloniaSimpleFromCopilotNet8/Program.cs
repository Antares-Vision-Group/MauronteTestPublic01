using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace avaloniaSimpleFromCopilotNet8
{
    // public class App : Application
    // {
    //     //public override void Initialize() => AvaloniaXamlLoader.Load(this);

    //     public override void OnFrameworkInitializationCompleted()
    //     {
    //         if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    //         {
    //             desktop.MainWindow = new MainWindow();
    //         }
    //         base.OnFrameworkInitializationCompleted();
    //     }
    // }

    // public partial class MainWindow : Window
    // {
    //     public MainWindow()
    //     {
    //         InitializeComponent();
    //     }

    //     private void InitializeComponent()
    //     {
    //         Title = "Avalonia Dev Container";
    //         Width = 400;
    //         Height = 300;
    //         Content = new TextBlock
    //         {
    //             Text = "Hello from Avalonia in a Dev Container!",
    //             VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
    //             HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
    //         };

    //     }


    // }

    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args) =>
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                      .UsePlatformDetect()
                      .LogToTrace();
    }
}
