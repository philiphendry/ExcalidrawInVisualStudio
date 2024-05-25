using System;
using System.IO;
using Microsoft.VisualStudio.Shell;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell.Interop;
using Debugger = System.Diagnostics.Debugger;
using Microsoft.VisualStudio;

[Guid("55415F2D-3595-4DA8-87DF-3F9388DAD6C2")]
public class ExcalidrawWindowPane : WindowPane, IVsPersistDocData
{
    private string _file;
    private bool _isDisposed;
    private readonly WebView2 _webView = new WebView2() { HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch };

    private readonly TaskCompletionSource<bool> _webViewInitialisedTaskSource = new TaskCompletionSource<bool>(false);

    public ExcalidrawWindowPane() : base(null)
    {
        base.Initialize();
        _webView.Initialized += WebView_Initialized;
    }
        
    private void WebView_Initialized(object sender, EventArgs e)
    {
        _webView.Initialized -= WebView_Initialized;
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
        try
        {
            //// TODO Hack to wait for Excalidraw to have loaded
            await Task.Delay(250);
            _webViewInitialisedTaskSource.SetResult(true);
        }
        catch (Exception exception)
        {
            var exceptionHtml = $"<p>An unexpected exception occurred:</p><pre>{exception.ToString().Replace("<", "&lt;").Replace("&", "&amp;")}</pre>";
            _webView.NavigateToString(exceptionHtml);
        }
    }

    private void LoadScene()
    {
        // TODO parse JSON to check it's the correct format
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await _webViewInitialisedTaskSource.Task;

            var sceneData = File.ReadAllText(_file);
            await _webView.ExecuteScriptAsync($"window.interop.loadScene({sceneData})");
        }).FileAndForget("excalidraw");
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
        _webView.Dispose();
        _isDisposed = true;
    }

    #region IVsPersistDocData
    private bool _isDirty;

    public int GetGuidEditorType(out Guid pClassID)
    {
        pClassID = new Guid("51C27119-216E-4656-BD87-DF82198AB01F");
        return VSConstants.S_OK;
    }

    public int IsDocDataDirty(out int pfDirty)
    {
        pfDirty = _isDirty ? 1 : 0;
        return VSConstants.S_OK;
    }

    public int SetUntitledDocPath(string pszDocDataPath)
    {
        _file = pszDocDataPath;
        return VSConstants.S_OK;
    }

    public int LoadDocData(string pszMkDocument)
    {
        _file = pszMkDocument;
        LoadScene();
        return VSConstants.S_OK;
    }

    public int SaveDocData(VSSAVEFLAGS dwSave, out string pbstrMkDocumentNew, out int pfSaveCanceled)
    {
        pbstrMkDocumentNew = null;
        pfSaveCanceled = 0;

        try
        {
            switch (dwSave)
            {
                case VSSAVEFLAGS.VSSAVE_Save:
                case VSSAVEFLAGS.VSSAVE_SilentSave:
                    ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                    {
                        var sceneData = await _webView.ExecuteScriptAsync("window.interop.getScene()");
                        File.WriteAllText(_file, sceneData);
                    }).FileAndForget("excalidraw");

                    break;

                case VSSAVEFLAGS.VSSAVE_SaveAs:
                case VSSAVEFLAGS.VSSAVE_SaveCopyAs:
                    
                    Debugger.Break();

                    // TODO: Implement your logic to handle "Save As" operations.
                    // This might involve showing a Save File dialog and then saving the data to the chosen file.
                    // string newPath = ShowSaveFileDialog();
                    // File.WriteAllText(newPath, serializedData);
                    // pbstrMkDocumentNew = newPath;
                    break;

                default:
                    return VSConstants.E_INVALIDARG;
            }

            // If the save operation was successful, clear the dirty flag.
            _isDirty = false;

            return VSConstants.S_OK;
        }
        catch (Exception ex)
        {
            // If an error occurs, return the error code.
            return Marshal.GetHRForException(ex);
        }
    }

    public int Close()
    {
        return VSConstants.S_OK;
    }

    public int OnRegisterDocData(uint docCookie, IVsHierarchy pHierNew, uint itemidNew)
    {
        return VSConstants.S_OK;
    }

    public int RenameDocData(uint grfAttribs, IVsHierarchy pHierNew, uint itemidNew, string pszMkDocumentNew)
    {
        return VSConstants.S_OK;
    }

    public int IsDocDataReloadable(out int pfReloadable)
    {
        pfReloadable = 1;
        return VSConstants.S_OK;
    }

    public int ReloadDocData(uint grfFlags)
    {
        try
        {
            LoadScene();

            // If the reload operation was successful, clear the dirty flag.
            _isDirty = false;

            return VSConstants.S_OK;
        }
        catch (Exception ex)
        {
            // If an error occurs, return the error code.
            return Marshal.GetHRForException(ex);
        }
    }

    #endregion
}
