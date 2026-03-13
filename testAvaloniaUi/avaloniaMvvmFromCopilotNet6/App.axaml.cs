using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace avaloniaMvvmFromCopilotNet6
{
    public partial class App : Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(this)
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        public void SetDarkTheme() => RequestedThemeVariant = ThemeVariant.Dark;
        public void SetLightTheme() => RequestedThemeVariant = ThemeVariant.Light;
    }
}
