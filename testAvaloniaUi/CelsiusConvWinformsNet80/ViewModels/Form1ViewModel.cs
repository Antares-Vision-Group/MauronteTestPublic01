using System;
using System.ComponentModel;
using CelsiusConvModelNetStd2;

namespace CelsiusConvWinformsNet80.ViewModels;

public class Form1ViewModel: INotifyPropertyChanged, IDisposable
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly MainWindowModel _model;

    private string? _celsiusString; //alwas updated by the UI and updates the CelsiusFloat
    private string? _fahrenheitString; //always updated by FahrenheitFloat and updates the UI

    public string Greeting { get; } = "Welcome to Windows Forms!";
    public string? CelsiusString //alwas updated by the UI and updates the CelsiusFloat
    {
        get => _celsiusString;
        set
        {
            if(_celsiusString != value)
            {
                _celsiusString = value;

                if (float.TryParse(_celsiusString, out float parsedCelsius))
                {
                    _model.CelsiusFloat = parsedCelsius;
                }
                else
                {
                    _model.CelsiusFloat = null;
                }

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CelsiusString)));

            }
        }
    }
    public string? FahrenheitString //always updated by FahrenheitFloat and updates the UI
    { 
        get => _fahrenheitString;
        set
        {
            if(_fahrenheitString != value)
            {
                _fahrenheitString = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FahrenheitString)));
            }    
        }
    }

    public bool CanCreateDriver
    {
        get => _model.CanCreateDriver;  
    } 

    public Form1ViewModel()
    {

        _model = new MainWindowModel();

        if(LicenseManager.UsageMode != LicenseUsageMode.Designtime)
        {
            //load configuration that should not be done at design time
        }

        _model.PropertyChanged += model_PropertyChanged;
    }

    public void CalculateFahrenheitFloats() => _model.CalculateFahrenheitFloats();

    public void CreateDriver() => _model.CreateDriver();

    public Task CreateDriverAsync(CancellationToken ct = default) => Task.Run(_model.CreateDriver, ct);

    public void Dispose()
    {
        _model.Dispose();
    }

    private void model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    { 
        switch (e.PropertyName)
        {
            case nameof(MainWindowModel.FahrenheitFloat):
                var fahrenheitValue = _model.FahrenheitFloat;
                FahrenheitString = fahrenheitValue.HasValue ? fahrenheitValue.Value.ToString() : string.Empty;
                break;

            case nameof(MainWindowModel.CanCreateDriver):
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanCreateDriver)));
                break;
        }

    }
 
}
