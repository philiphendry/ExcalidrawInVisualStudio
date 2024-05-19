using System.IO;
using Microsoft.VisualStudio.Shell;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Reflection;

[Guid("55415F2D-3595-4DA8-87DF-3F9388DAD6C2")]
public class ExcalidrawWindowPane : ToolWindowPane
{
    private readonly string _file;
    private readonly WebView2 _webView = new WebView2() { HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch };

    public ExcalidrawWindowPane(string file) : base(null)
    {
        base.Initialize();
        
        _file = file;

        Caption = "Excalidraw";
        BitmapResourceID = 301;
        BitmapIndex = 1;

        _webView.Initialized += _webView_Initialized;
    }

    private void _webView_Initialized(object sender, System.EventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Assembly.GetExecutingAssembly().GetName().Name);
            var webView2Environment = CoreWebView2Environment.CreateAsync(null, tempDir, null).GetAwaiter().GetResult();
            await _webView.EnsureCoreWebView2Async(webView2Environment);
            _webView.NavigateToString("<html><body><h1>Hello, Excalidraw!</h1></body></html>");
        });
    }

    protected override void Initialize()
    {
        Content = _webView;
    }
}
