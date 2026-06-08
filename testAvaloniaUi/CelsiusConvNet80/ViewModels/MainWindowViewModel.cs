using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using CelsiusConvModelNetStd2;
using ReactiveUI;
namespace CelsiusConvNet80.ViewModels;

public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly MainWindowModel _model;
    private MainWindowModel Model => _model;


    private string? _celsiusString; //alwas updated by the UI and updates the CelsiusFloat
    private readonly IDisposable _celsiusStringSubscription;

    private string? _fahrenheitString; //always updated by FahrenheitFloat and updates the UI
    private readonly IDisposable _fahrenheitFloatSubscription;

    private bool _canCreateDriver;

    private readonly IObservable<bool> _model_canCreateDriverObservable;

    private readonly IDisposable _model_canCreateDriverSubscription;

    public string Greeting { get; } = "Welcome to Avalonia!";
    public string? CelsiusString //alwas updated by the UI and updates the CelsiusFloat
    {
      get => _celsiusString;
      set => this.RaiseAndSetIfChanged(ref _celsiusString, value);
    }
    public string? FahrenheitString //always updated by FahrenheitFloat and updates the UI
    { 
        get => _fahrenheitString;
        set => this.RaiseAndSetIfChanged(ref _fahrenheitString, value);
    }

    public bool CanCreateDriver
    {
        get => _canCreateDriver;
        set => this.RaiseAndSetIfChanged(ref _canCreateDriver, value);
    }

    public ICommand CalculateFahrenheitFloatsCommand => ReactiveCommand.Create(CalculateFahrenheitFloats);
    
    public ICommand CreateDriverCommand {get;} 

    private string _errorMessage;

    public string ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    private void CalculateFahrenheitFloats() => _model.CalculateFahrenheitFloats();

    private async Task CreateDriverAsync(CancellationToken cancellationToken = default)
    {
        ErrorMessage = "Creating driver...";
        await Task.Run(_model.CreateDriver, cancellationToken);
        ErrorMessage = "Driver created successfully.";
    }


    public void Dispose()
    {
        _model_canCreateDriverSubscription.Dispose();
        _fahrenheitFloatSubscription.Dispose();
        _celsiusStringSubscription.Dispose();
        _model.Dispose();
    }

    public MainWindowViewModel()
    {

        _model = new MainWindowModel();
        
        _errorMessage = string.Empty;

        if (!Design.IsDesignMode)
        {
            //load configurations tha only work in not design mode
        }


        _model_canCreateDriverObservable = this.WhenAnyValue(x => x.Model.CanCreateDriver);

        _model_canCreateDriverSubscription = _model_canCreateDriverObservable.Subscribe(model_CanCreateDriver_Changed);

        _fahrenheitFloatSubscription = this.WhenAnyValue(x => x.Model.FahrenheitFloat).Subscribe(model_FahrenheitFloat_Changed);

        _celsiusStringSubscription = this.WhenAnyValue(x => x.CelsiusString).Subscribe(CelsiusString_Changed);

        var createDriverCommand = ReactiveCommand.CreateFromTask(CreateDriverAsync, _model_canCreateDriverObservable); //ReactiveCommand.Create(CreateDriver, _model_canCreateDriverObservable);
        createDriverCommand.ThrownExceptions.Subscribe(model_CreateDriver_Error);
        CreateDriverCommand = createDriverCommand;
    }

    private void CelsiusString_Changed(string? newValue)
    {
        if (float.TryParse(newValue, out float parsedCelsius))
        {
            _model.CelsiusFloat = parsedCelsius;
        }
        else
        {
            _model.CelsiusFloat = null;
        }
    }

    private void model_CanCreateDriver_Changed(bool canCreate)
    {
        CanCreateDriver = canCreate;
    }

    private void model_FahrenheitFloat_Changed(float? fahrenheit)
    {
        FahrenheitString = fahrenheit.HasValue ? fahrenheit.Value.ToString("0.0") : null;
    }

    private void model_CreateDriver_Error(Exception ex)
    {
        ErrorMessage = ex.Message;
    }

}
