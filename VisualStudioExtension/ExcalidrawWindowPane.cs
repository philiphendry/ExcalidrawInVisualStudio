using System;
using System.IO;
using Microsoft.VisualStudio.Shell;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Reflection;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Debugger = System.Diagnostics.Debugger;

[Guid("55415F2D-3595-4DA8-87DF-3F9388DAD6C2")]
public class ExcalidrawWindowPane : ToolWindowPane
{
    private readonly string _file;
    private bool _isDisposed;
    private readonly WebView2 _webView = new WebView2() { HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch };

    private readonly DTE2 _dte;
    private DocumentEvents _documentEvents;

    public ExcalidrawWindowPane(DTE2 dte, string file) : base(null)
    {
        base.Initialize();
        
        _file = file;
        _dte = dte;

        BitmapImageMoniker = new Microsoft.VisualStudio.Imaging.Interop.ImageMoniker
        {
            Guid = new Guid("b2f7f8e2-4687-4f3b-8b3b-7f4d5f9d5d3a"),
            Id = 301
        };
        BitmapIndex = 1;

        _webView.Initialized += _webView_Initialized;

        InitialiseDocumentEventsAsync().ConfigureAwait(false);
    }

    private async Task InitialiseDocumentEventsAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();        

        _documentEvents = _dte.Events.DocumentEvents;
        _documentEvents.DocumentSaved += DocumentEvents_DocumentSaved;
    }

    private void DocumentEvents_DocumentSaved(Document document)
    {
        
    }

    private void _webView_Initialized(object sender, EventArgs e)
    {        
        InitialiseBrowserAsync();
    }

    private async Task InitialiseBrowserAsync()
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Assembly.GetExecutingAssembly().GetName().Name);
            var webView2Environment = await CoreWebView2Environment.CreateAsync(null, tempDir, null);
            await _webView.EnsureCoreWebView2Async(webView2Environment);

            _webView.CoreWebView2.DOMContentLoaded += CoreWebView2_DOMContentLoaded;
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping("excalidraw-editor-host", Path.Combine(GetFolder(), "editor"), CoreWebView2HostResourceAccessKind.Allow);

            if (Debugger.IsAttached)
            {
                _webView.CoreWebView2.OpenDevToolsWindow();
            }

            var indexHtml = Path.Combine(GetFolder(), "editor", "index.html");
            _webView.NavigateToString(File.ReadAllText(indexHtml));
        });
    }

    private async void CoreWebView2_DOMContentLoaded(object sender, CoreWebView2DOMContentLoadedEventArgs e)
    {
        // TODO parse JSON to check it's the correct format
        try
        {
            // TODO Hack to wait for Excalidraw to have loaded
            await Task.Delay(250);
            await _webView.ExecuteScriptAsync($"window.interop.load({File.ReadAllText(_file)})");
        }
        catch (Exception exception)
        {
            var exceptionHtml = $"<p>An unexpected exception occurred:</p><pre>{exception.ToString().Replace("<", "&lt;").Replace("&", "&amp;")}</pre>";
            _webView.NavigateToString(exceptionHtml);
        }
    }

    public static string GetFolder()
    {
        var assembly = Assembly.GetExecutingAssembly().Location;
        return Path.GetDirectoryName(assembly);
    }

    protected override void Initialize()
    {
        Content = _webView;
    }
    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }
        _webView.Initialized -= _webView_Initialized;
        _webView.CoreWebView2.DOMContentLoaded -= CoreWebView2_DOMContentLoaded;
        _webView.Dispose();
        _documentEvents.DocumentSaved -= DocumentEvents_DocumentSaved;
        _isDisposed = true;
    }
}
