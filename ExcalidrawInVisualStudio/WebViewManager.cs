using CefSharp.Wpf;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;
using CefSharp;
using Debugger = System.Diagnostics.Debugger;
using System.Reflection;
using System.Windows.Controls;

namespace ExcalidrawInVisualStudio;

/// <summary>
/// The purpose of the WebViewManager class is to manage a web view control (WebView2) that hosts the Excalidraw editor.
/// It handles the initialization of the web view, sets up event handlers for key presses and web message received,
/// and provides methods for interacting with the web view, such as getting the scene, loading a library, and loading
/// a scene. The class also implements the IDisposable interface to properly dispose of the web view control when it is no longer needed.
/// </summary>
public class WebViewManager : IDisposable
{
    private readonly ChromiumWebBrowser _webView;
    private readonly ExtensionConfiguration _extensionConfiguration = new();

    public EventHandler OnDirty;
    public EventHandler OnReady;
    public EventHandler<LibraryChangeEventArgs> OnLibraryChange;
    public EventHandler<KeyPressEventArgs> OnKeyPress;
    public EventHandler<SceneSaveEventArgs> OnSceneSave;

    public WebViewManager()
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
        _webView = new() { HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch };
        _webView.Initialized += WebView_Initialized;
    }

    private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
    {
        var assemblyName = new AssemblyName(args.Name).Name + ".dll";
        var assemblyFolder = new FileInfo(typeof(WebViewManager).Assembly.Location).DirectoryName;
        var assemblyPath = Path.Combine(assemblyFolder, "x64", assemblyName);
        if (File.Exists(assemblyPath))
        {
            return Assembly.LoadFrom(assemblyPath);
        }
        return null;
    }

    public object Content => _webView;

    private void WebView_Initialized(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _webView.Initialized -= WebView_Initialized;
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            _webView.Address = "https://www.google.com";
            //var webView2Environment = await CoreWebView2Environment.CreateAsync(null, _extensionConfiguration.GetUserDataFolder());
            //await _webView.EnsureCoreWebView2Async(webView2Environment);

            _webView.KeyDown += WebView_KeyDown;
            //_webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            //_webView.CoreWebView2.SetVirtualHostNameToFolderMapping("excalidraw-editor-host", _extensionConfiguration.GetEditorSiteFolder(), CoreWebView2HostResourceAccessKind.Allow);

            //if (Debugger.IsAttached)
            //{
            //    _webView.CoreWebView2.OpenDevToolsWindow();
            //}

            var indexHtmlPath = Path.Combine(_extensionConfiguration.GetEditorSiteFolder(), "index.html");
            var indexHtmlContent = File.ReadAllText(indexHtmlPath);
            indexHtmlContent = indexHtmlContent
                .Replace("<!--replace-with-web-view-base-url-->", "<base href=\"http://excalidraw-editor-host/\" />")
                .Replace("replace-with-export-source", Constants.MarketplaceUrl);

            indexHtmlContent = indexHtmlContent.Replace("replace-with-theme", _extensionConfiguration.GetVsTheme());

            //_webView.NavigateToString(indexHtmlContent);
        }).FileAndForget("excalidraw");
    }

    public async Task ThemeChangedAsync()
    {
        //await _webView.ExecuteScriptAsync($"window.interop.setTheme(\"{_extensionConfiguration.GetVsTheme()}\")");

    }

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

    //private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    //{
    //    try
    //    {
    //        var json = e.WebMessageAsJson;
    //        var document = JsonDocument.Parse(json);
    //        var root = document.RootElement;
    //        var eventType = root.GetProperty("event").GetString();
    //        switch (eventType)
    //        {
    //            case "onChange":
    //                OnDirty?.Invoke(this, EventArgs.Empty);
    //                break;
    //            case "onReady":
    //                OnReady?.Invoke(this, EventArgs.Empty);
    //                break;
    //            case "onLibraryChange":
    //                OnLibraryChange?.Invoke(this, new LibraryChangeEventArgs {  LibraryItems = root.GetProperty("libraryItems").GetRawText() });
    //                break;
    //            case "onSceneSave":
    //                var contentType = root.GetProperty("contentType").GetString();
    //                var data = root.GetProperty("data").Deserialize<uint[]>().Select(i => (byte)i).ToArray();
    //                OnSceneSave?.Invoke(this, new SceneSaveEventArgs { Data = data, ContentType = contentType });
    //                break;
    //        }
    //    }
    //    catch (Exception exception)
    //    {
    //        Trace.WriteLine($"Excalidraw: Error in CoreWebView2_WebMessageReceived: {exception}");
    //    }
    //}

    /// <summary>
    /// If WebView2 supported async methods then we could await the return of the saveSceneAsync method. Instead, the call
    /// to saveSceneAsync will return immediately and the result will be available in the <see cref="OnSceneSave"/> event handler.
    /// </summary>
    public async Task SaveSceneJsonAsync()
    {
        //await _webView.ExecuteScriptAsync($"window.interop.saveSceneAsync('application/json')");
    }

    public async Task SaveScenePngAsync()
    {
        //await _webView.ExecuteScriptAsync($"window.interop.saveSceneAsync('image/png')");
    }

    public async Task LoadLibraryAsync(string libraryItems)
    {
        //await _webView.ExecuteScriptAsync($"window.interop.loadLibrary({libraryItems})");
    }

    public async Task LoadSceneJsonAsync(string sceneData)
    {
        //await _webView.ExecuteScriptAsync($"window.interop.loadSceneAsync({sceneData}, 'application/json')");
    }

    public async Task LoadScenePngAsync(byte[] sceneData)
    {
        //await _webView.ExecuteScriptAsync($"window.interop.loadSceneAsync({JsonSerializer.Serialize(sceneData.ToList())}, 'image/png')");
    }

    public void Dispose()
    {
        //_webView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
        AppDomain.CurrentDomain.AssemblyResolve -= ResolveAssembly;
        _webView.KeyDown -= WebView_KeyDown;
        _webView?.Dispose();
    }
}