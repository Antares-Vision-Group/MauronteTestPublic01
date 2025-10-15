using System;
using Microsoft.Web.WebView2.Wpf;

namespace WpfNet6;

public class MainWindowViewModel
{
    public string WebView2UserDataFolder => System.IO.Path.Combine(Environment.CurrentDirectory, "WebView2UserDataFolder");

    public CoreWebView2CreationProperties WebView2CreationProperties => new CoreWebView2CreationProperties()
    {
        UserDataFolder = WebView2UserDataFolder
    };

    public Uri InitialUri => new Uri("https://microsoft.com", UriKind.Absolute);
}


