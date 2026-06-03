using System;
using System.ComponentModel;

namespace CelsiusConvModelNetStd2
{
    public class MainWindowModel: INotifyPropertyChanged, IDisposable
    {
        private float? _celsiusFloat; //always updated by CelsiusString
        private float? _fahrenheitFloat; //always updated by Calculations and updates the FahrenheitString

        private bool _canCreateDriver = true;
        public float? CelsiusFloat //always updated by CelsiusString
        {
            get => _celsiusFloat;
            set
            {
                if (_celsiusFloat != value)
                {
                    _celsiusFloat = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CelsiusFloat)));
                }
            }
        }

        public float? FahrenheitFloat //always updated by Calculations and updates the FahrenheitString
        { 
            get => _fahrenheitFloat;
        }

        public bool CanCreateDriver
        {
            get => _canCreateDriver;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void CalculateFahrenheitFloats()
        {
            float? calculatedFahrenheit;
            if(_celsiusFloat.HasValue)
                calculatedFahrenheit = _celsiusFloat.Value * (9f / 5f) + 32;
            else
                calculatedFahrenheit = null;

            if(calculatedFahrenheit != _fahrenheitFloat)
            {
                _fahrenheitFloat = calculatedFahrenheit;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FahrenheitFloat)));
            }

        }


        public virtual void CreateDriver() 
        {
            if(_canCreateDriver)
            {
                _canCreateDriver = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanCreateDriver)));
            }
        }
        
        public void Dispose()
        {
            
        }

    }
}
