using Avalonia.Threading;
using ReactiveUI;
using System;
using System.Reactive;
using System.Reactive.Linq;

namespace avaloniaMvvmFromCopilotNet6
{
    public class MainWindowViewModel : ReactiveObject
    {
        private string _greeting = "Hello Avalonia 11 on .NET 6!";
        private readonly App _app;

        public string Greeting
        {
            get => _greeting;
            set => this.RaiseAndSetIfChanged(ref _greeting, value);
        }

        public ReactiveCommand<Unit, Unit> ClickCommand { get; }
        public ReactiveCommand<Unit, Unit> LightThemeCommand { get; }
        public ReactiveCommand<Unit, Unit> DarkThemeCommand { get; }

        public MainWindowViewModel(App app)
        {
            _app = app;

            ClickCommand = ReactiveCommand.Create(() =>
            
                {Greeting = $"Button clicked at {DateTime.Now:T}";}
                //return $"Button clicked at {DateTime.Now:T}";
            );

            
            LightThemeCommand = ReactiveCommand.Create(() => _app.SetLightTheme());
            DarkThemeCommand = ReactiveCommand.Create(() => _app.SetDarkTheme());
            
        }
    }
}
