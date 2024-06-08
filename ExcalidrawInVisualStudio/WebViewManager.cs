using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Debugger = System.Diagnostics.Debugger;

namespace ExcalidrawInVisualStudio;

/// <summary>
/// The purpose of the WebViewManager class is to manage a web view control (WebView2) that hosts the Excalidraw editor.
/// It handles the initialization of the web view, sets up event handlers for key presses and web message received,
/// and provides methods for interacting with the web view, such as getting the scene, loading a library, and loading
/// a scene. The class also implements the IDisposable interface to properly dispose of the web view control when it is no longer needed.
/// </summary>
public class WebViewManager : IDisposable
{
    private readonly WebView2 _webView = new() { HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch };
    private readonly ExtensionConfiguration _extensionConfiguration = new();

    public EventHandler OnDirty;
    public EventHandler OnReady;
    public EventHandler<LibraryChangeEventArgs> OnLibraryChange;
    public EventHandler<KeyPressEventArgs> OnKeyPress;

    public WebViewManager()
    {
        _webView.Initialized += WebView_Initialized;
    }

    public object Content => _webView;

    private void WebView_Initialized(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _webView.Initialized -= WebView_Initialized;
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            var webView2Environment = await CoreWebView2Environment.CreateAsync(null, _extensionConfiguration.GetUserDataFolder());
            await _webView.EnsureCoreWebView2Async(webView2Environment);

            _webView.KeyDown += WebView_KeyDown;
            _webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping("excalidraw-editor-host", _extensionConfiguration.GetEditorSiteFolder(), CoreWebView2HostResourceAccessKind.Allow);

            if (Debugger.IsAttached)
            {
                _webView.CoreWebView2.OpenDevToolsWindow();
            }

            var indexHtmlPath = Path.Combine(_extensionConfiguration.GetEditorSiteFolder(), "index.html");
            var indexHtmlContent = File.ReadAllText(indexHtmlPath);
            indexHtmlContent = indexHtmlContent
                .Replace("<!--replace-with-web-view-base-url-->", "<base href=\"http://excalidraw-editor-host/\" />")
                .Replace("replace-with-export-source", Constants.MarketplaceUrl);

            indexHtmlContent = indexHtmlContent.Replace("replace-with-theme", _extensionConfiguration.GetVsTheme());

            _webView.NavigateToString(indexHtmlContent);
        }).FileAndForget("excalidraw");
    }

    public async Task ThemeChangedAsync() => await _webView.ExecuteScriptAsync($"window.interop.setTheme(\"{_extensionConfiguration.GetVsTheme()}\")");

    private void WebView_KeyDown(object sender, KeyEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var binding = string.Empty;
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            binding += "Ctrl+";
        }
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            binding += "Shift+";
        }
        if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
        {
            binding += "Alt+";
        }
        binding += e.Key.ToString();

        OnKeyPress?.Invoke(this, new KeyPressEventArgs { KeyPress = binding });
    }

    private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;
            var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var eventType = root.GetProperty("event").GetString();
            if (eventType == "onChange")
            {
                OnDirty?.Invoke(this, EventArgs.Empty);
            }
            else if (eventType == "onReady")
            {
                OnReady?.Invoke(this, EventArgs.Empty);
            }
            else if (eventType == "onLibraryChange")
            {
                var libraryItems = root.GetProperty("libraryItems").GetRawText();
                OnLibraryChange?.Invoke(this, new LibraryChangeEventArgs {  LibraryItems = libraryItems });
            }
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"Excalidraw: Error in CoreWebView2_WebMessageReceived: {exception}");
        }
    }

    public async Task<string> GetSceneAsync() => await _webView.ExecuteScriptAsync("window.interop.getScene()");

    public async Task LoadLibraryAsync(string libraryItems) => await _webView.ExecuteScriptAsync($"window.interop.loadLibrary({libraryItems})");

    public async Task LoadSceneAsync(string sceneData) => await _webView.ExecuteScriptAsync($"window.interop.loadScene({sceneData})");

    public void Dispose()
    {
        _webView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
        _webView.KeyDown -= WebView_KeyDown;
        _webView?.Dispose();
    }
}