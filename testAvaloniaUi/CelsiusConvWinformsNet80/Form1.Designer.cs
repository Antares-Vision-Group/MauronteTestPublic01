namespace CelsiusConvWinformsNet80;

partial class Form1
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer _components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _components?.Dispose();
                _viewModel?.Dispose();   
            }

            base.Dispose(disposing);
        }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        _components = new System.ComponentModel.Container();
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(800, 450);
        Text = "Form1";

        this._bindingSource = new BindingSource(this._components);

            this._celsiusTextBox = new System.Windows.Forms.TextBox();
            this._celsiusTextBox.Location = new System.Drawing.Point(50, 50);
            this._celsiusTextBox.Name = "Celsius";
            this._celsiusTextBox.DataBindings.Add("Text", this._bindingSource, "CelsiusString", false, DataSourceUpdateMode.OnPropertyChanged, string.Empty);
            this.Controls.Add(this._celsiusTextBox);

            this._fahrenheitTextBox = new System.Windows.Forms.TextBox();
            this._fahrenheitTextBox.Location = new System.Drawing.Point(50, 100);
            this._fahrenheitTextBox.ReadOnly = true;
            this._fahrenheitTextBox.Name = "Fahrenheit";
            this._fahrenheitTextBox.DataBindings.Add("Text", this._bindingSource, "FahrenheitString", false, DataSourceUpdateMode.OnPropertyChanged, string.Empty);
            this.Controls.Add(this._fahrenheitTextBox); 

            this._calculateFloatsButton = new System.Windows.Forms.Button();
            this._calculateFloatsButton.Location = new System.Drawing.Point(50, 150);
            this._calculateFloatsButton.Name = "CalculateFloats";
            this._calculateFloatsButton.Text = "Calculate";
            this._calculateFloatsButton.Click += new System.EventHandler(this._calculateFloatsButton_Click);
            this.Controls.Add(this._calculateFloatsButton);

            this._createDriverButton = new System.Windows.Forms.Button();
            this._createDriverButton.Location = new System.Drawing.Point(200, 150);
            this._createDriverButton.Name = "CreateDriver";
            this._createDriverButton.Text = "Create Driver";
            this._createDriverButton.Click += new System.EventHandler(this._createDriverButton_Click);
            this._createDriverButton.DataBindings.Add("Enabled", this._bindingSource, "CanCreateDriver", false, DataSourceUpdateMode.OnPropertyChanged);
            this.Controls.Add(this._createDriverButton);

    }

    private TextBox _celsiusTextBox;
    private TextBox _fahrenheitTextBox;

    private Button _calculateFloatsButton;

    private Button _createDriverButton;

    private BindingSource _bindingSource;

    #endregion
}
