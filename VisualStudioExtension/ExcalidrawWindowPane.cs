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
using System.Diagnostics;
using System.Text.Json;
using Microsoft.VisualStudio.Threading;

[Guid("55415F2D-3595-4DA8-87DF-3F9388DAD6C2")]
public class ExcalidrawWindowPane : WindowPane, IVsPersistDocData
{
    private string _filename;
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

            _webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping("excalidraw-editor-host", Path.Combine(GetFolder(), "editor"), CoreWebView2HostResourceAccessKind.Allow);

            if (Debugger.IsAttached)
            {
                _webView.CoreWebView2.OpenDevToolsWindow();
            }

            var indexHtmlPath = Path.Combine(GetFolder(), "editor", "index.html");
            var indexHtmlContent = File.ReadAllText(indexHtmlPath);
            indexHtmlContent = indexHtmlContent.Replace("<!--replace-with-web-view-base-url-->", "<base href=\"http://excalidraw-editor-host/\" />");
            _webView.NavigateToString(indexHtmlContent);
        });
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
                _isDirty = true;
            } 
            else if (eventType == "onReady")
            {
                // Set the TaskCompletionSource to true to indicate the WebView has been initialised
                _webViewInitialisedTaskSource.SetResult(true);
            }
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"Error in CoreWebView2_WebMessageReceived: {exception}");
        }
    }

    private void LoadScene()
    {
        _isDirty = false;

        // TODO parse JSON to check it's the correct format
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            // Wait for the WebView to be initialised
            await _webViewInitialisedTaskSource.Task.WithTimeout(TimeSpan.FromSeconds(2));

            var sceneData = File.ReadAllText(_filename);
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
        _webView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
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
        _filename = pszDocDataPath;
        return VSConstants.S_OK;
    }

    public int LoadDocData(string pszMkDocument)
    {
        _filename = pszMkDocument;
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
                        File.WriteAllText(_filename, sceneData);
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
