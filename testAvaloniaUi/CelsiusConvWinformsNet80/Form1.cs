using CelsiusConvWinformsNet80.ViewModels;

namespace CelsiusConvWinformsNet80;

public partial class Form1 : Form
{
 
    private readonly Form1ViewModel _viewModel;

    public Form1()
    {
        _viewModel = new Form1ViewModel();
        InitializeComponent();
        this._bindingSource.DataSource = _viewModel;
    }

    private void _calculateFloatsButton_Click(object sender, EventArgs e)
    {
        _viewModel.CalculateFahrenheitFloats();
    }

    private async void _createDriverButton_Click(object sender, EventArgs e)
    {
        
        CancellationTokenSource cts = new CancellationTokenSource(5000);
        
        try
        {
            await _viewModel.CreateDriverAsync(cts.Token);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error creating driver: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            cts.Dispose();
        }

    }


}
