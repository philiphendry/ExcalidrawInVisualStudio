using System.IO;
using Microsoft.VisualStudio.Shell;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Reflection;
using System.Threading.Tasks;

[Guid("55415F2D-3595-4DA8-87DF-3F9388DAD6C2")]
public class ExcalidrawWindowPane : ToolWindowPane
{
    private readonly string _file;
    private bool _isDisposed;
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
            var webView2Environment = await CoreWebView2Environment.CreateAsync(null, tempDir, null);
            await _webView.EnsureCoreWebView2Async(webView2Environment);

            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping("excalidraw-editor-host", Path.Combine(GetFolder(), "editor"), CoreWebView2HostResourceAccessKind.Allow);

            var indexHtml = Path.Combine(GetFolder(), "editor", "index.html");
            _webView.NavigateToString(File.ReadAllText(indexHtml));
            
        });
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

    public void Dispose()
    {
        if(_isDisposed)
        {
            return;
        }
        _webView.Dispose();
    }
}
