using Microsoft.Web.WebView2.Core;

namespace WinFormsNet6;

public partial class Form1 : Form
{
    public Form1()
    {
        InitializeComponent();
        this.Resize += new System.EventHandler(this.Form_Resize);
        webView.NavigationStarting += EnsureHttps;
        InitializeAsync();

    }

    private void Form_Resize(object? sender, EventArgs e)
    {
        webView.Size = this.ClientSize - new System.Drawing.Size(webView.Location);
        goButton.Left = this.ClientSize.Width - goButton.Width;
        addressBar.Width = goButton.Left - addressBar.Left;
    }

    void EnsureHttps(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        String uri = args.Uri;
        if (!uri.StartsWith("https://"))
        {
            webView.CoreWebView2.ExecuteScriptAsync($"alert('{uri} is not safe, try an https link')");
            args.Cancel = true;
        }
    }

    async void InitializeAsync()
    {
        await Task.Yield();

        await webView.EnsureCoreWebView2Async(null);

        webView.CoreWebView2.WebMessageReceived += UpdateAddressBar;

        await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.chrome.webview.postMessage(window.document.URL);");
        await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.chrome.webview.addEventListener(\'message\', event => alert(event.data));");

    }

    void UpdateAddressBar(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        String uri = args.TryGetWebMessageAsString();
        addressBar.Text = uri;
        webView.CoreWebView2.PostWebMessageAsString(uri);
    }

    private void goButton_Click(object sender, EventArgs e)
    {
        if (webView != null && webView.CoreWebView2 != null)
        {
            webView.CoreWebView2.Navigate(addressBar.Text);
        }
    }

    private void Form1_Load(object sender, EventArgs e)
    {

    }

    private void webView21_Click(object sender, EventArgs e)
    {

    }

    private void webView_CoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            //this.webView.CreationProperties.UserDataFolder = System.IO.Path.Combine(Environment.CurrentDirectory, "WebView2UserDataFolder");            
            webView.CoreWebView2.Navigate("https://microsoft.com");
        }
        else
        {
            MessageBox.Show($"WebView2 initialization failed: {e.InitializationException}");
        }
    }

}
