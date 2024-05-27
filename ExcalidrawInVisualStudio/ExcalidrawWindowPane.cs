using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.VisualStudio.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Debugger = System.Diagnostics.Debugger;

namespace ExcalidrawInVisualStudio;

[Guid("55415F2D-3595-4DA8-87DF-3F9388DAD6C2")]
public class ExcalidrawWindowPane : WindowPane, IVsPersistDocData, IVsFileChangeEvents, IVsDocDataFileChangeControl
{
    private readonly WebView2 _webView = new WebView2() { HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch };
    private string _filename;
    private bool _isDisposed;
    private bool _isDirty;
    private IVsUIShell _uiShell;

    // Counter of the file system changes to ignore.
    private int changesToIgnore;
    private IVsFileChangeEx vsFileChangeEx;
    private Timer reloadTimer = new Timer();
    private bool fileChangedTimerSet;

    // Cookie for the subscription to the file system notification events.
    private uint vsFileChangeCookie;

    private readonly TaskCompletionSource<bool> _webViewInitialisedTaskSource = new TaskCompletionSource<bool>(false);

    public ExcalidrawWindowPane() : base(null)
    {
        base.Initialize();
        _webView.Initialized += WebView_Initialized;        
    }

    private void WebView_Initialized(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _uiShell = (IVsUIShell)GetService(typeof(SVsUIShell));

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
        }).FileAndForget("excalidraw");
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
            SetFileChangeNotification(_filename, false);

            // Wait for the WebView to be initialised
            await _webViewInitialisedTaskSource.Task.WithTimeout(TimeSpan.FromSeconds(2));

            var sceneData = File.ReadAllText(_filename);
            await _webView.ExecuteScriptAsync($"window.interop.loadScene({sceneData})");

            SetFileChangeNotification(_filename, true);

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

    public int GetGuidEditorType(out Guid pClassID)
    {
        pClassID = new Guid("51C27119-216E-4656-BD87-DF82198AB01F");
        return VSConstants.S_OK;
    }

    public int IsDocDataDirty(out int pfDirty)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        pfDirty = _isDirty ? 1 : 0;
        return VSConstants.S_OK;
    }

    public int SetUntitledDocPath(string pszDocDataPath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _filename = pszDocDataPath;
        return VSConstants.S_OK;
    }

    public int LoadDocData(string pszMkDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _filename = pszMkDocument;
        LoadScene();
        return VSConstants.S_OK;
    }

    public int SaveDocData(VSSAVEFLAGS dwSave, out string pbstrMkDocumentNew, out int pfSaveCanceled)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        pbstrMkDocumentNew = null;
        pfSaveCanceled = 0;

        try
        {
            SetFileChangeNotification(_filename, false);
            switch (dwSave)
            {
                case VSSAVEFLAGS.VSSAVE_Save:
                case VSSAVEFLAGS.VSSAVE_SilentSave:
                    ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                    {
                        var sceneData = await _webView.ExecuteScriptAsync("window.interop.getScene()");
                        File.WriteAllText(_filename, sceneData);

                        SetFileChangeNotification(_filename, true);
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
        ThreadHelper.ThrowIfNotOnUIThread();

        SetFileChangeNotification(_filename, false);

        return VSConstants.S_OK;
    }

    public int OnRegisterDocData(uint docCookie, IVsHierarchy pHierNew, uint itemidNew)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return VSConstants.S_OK;
    }

    public int RenameDocData(uint grfAttribs, IVsHierarchy pHierNew, uint itemidNew, string pszMkDocumentNew)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        SetFileChangeNotification(_filename, false);
        _filename = pszMkDocumentNew;
        SetFileChangeNotification(_filename, true);
        return VSConstants.S_OK;
    }

    public int IsDocDataReloadable(out int pfReloadable)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        pfReloadable = 1;
        return VSConstants.S_OK;
    }

    public int ReloadDocData(uint grfFlags)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
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

    #region IVsFileChangeEvents

    public int FilesChanged(uint numberOfChanges, string[] filesChanged, uint[] typesOfChanges)
    {
        Trace.WriteLine(string.Format(CultureInfo.CurrentCulture, "\t**** Inside FilesChanged ****"));

        if (0 == numberOfChanges || null == filesChanged || null == typesOfChanges)
            return VSConstants.E_INVALIDARG;

        //ignore file changes if we are in that mode
        if (changesToIgnore != 0)
            return VSConstants.S_OK;

        for (uint i = 0; i < numberOfChanges; i++)
        {
            if (string.IsNullOrEmpty(filesChanged[i]) ||
                string.Compare(filesChanged[i], _filename, true, CultureInfo.CurrentCulture) != 0)
            {
                continue;
            }
            // if it looks like the file contents have changed (either the size or the modified
            // time has changed) then we need to prompt the user to see if we should reload the
            // file. it is important to not synchronously reload the file inside of this FilesChanged
            // notification. first it is possible that there will be more than one FilesChanged 
            // notification being sent (sometimes you get separate notifications for file attribute
            // changing and file size/time changing). also it is the preferred UI style to not
            // prompt the user until the user re-activates the environment application window.
            // this is why we use a timer to delay prompting the user.
            if (0 != (typesOfChanges[i] & (int)(_VSFILECHANGEFLAGS.VSFILECHG_Time | _VSFILECHANGEFLAGS.VSFILECHG_Size)))
            {
                if (!fileChangedTimerSet)
                {
                    reloadTimer = new Timer();
                    fileChangedTimerSet = true;
                    reloadTimer.Interval = 1000;
                    reloadTimer.Tick += OnFileChangeEvent;
                    reloadTimer.Enabled = true;
                }
            }
        }

        return VSConstants.S_OK;
    }

    public int DirectoryChanged(string pszDirectory)
    {
        throw new NotImplementedException();
    }

    #endregion

    #region IVsDocDataFileChangeControl

    /// <summary>
    /// Called by the shell to notify if a file change must be ignored.
    /// </summary>
    /// <param name="ignoreFlag">Flag not zero if the file change must be ignored.</param>
    public int IgnoreFileChanges(int ignoreFlag)
    {
        if (0 != ignoreFlag)
        {
            // The changes must be ignored, so increase the counter of changes to ignore
            ++changesToIgnore;
        }
        else
        {
            if (changesToIgnore > 0)
            {
                --changesToIgnore;
            }
        }

        return VSConstants.S_OK;
    }

    #endregion

    /// <summary>
    /// This event is triggered when one of the files loaded into the environment has changed outside of the
    /// codeWindowHost
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnFileChangeEvent(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        reloadTimer.Enabled = false;
        var message = _filename + Environment.NewLine + Environment.NewLine + "This file has changed outside the editor. Do you wish to reload it?";
        var title = string.Empty;
        var result = 0;
        var tempGuid = Guid.Empty;
        _uiShell?.ShowMessageBox(0,
            ref tempGuid,
            title,
            message,
            null,
            0,
            OLEMSGBUTTON.OLEMSGBUTTON_YESNOCANCEL,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST,
            OLEMSGICON.OLEMSGICON_QUERY,
            0,
            out result);
        if (result == (int)DialogResult.Yes)
        {
            ((IVsPersistDocData)this).ReloadDocData(0);
        }
        fileChangedTimerSet = false;
    }

    /// <summary>
    /// In this function we inform the shell when we wish to receive 
    /// events when our file is changed or we inform the shell when 
    /// we wish not to receive events anymore.
    /// </summary>
    /// <param name="fileNameToNotify">File name string</param>
    /// <param name="startNotify">TRUE indicates advise, FALSE indicates unadvise.</param>
    /// <returns>Result of the operation</returns>
    private int SetFileChangeNotification(string fileNameToNotify, bool startNotify)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Trace.WriteLine(string.Format(CultureInfo.CurrentCulture, "\t **** Inside SetFileChangeNotification ****"));

        int result = VSConstants.E_FAIL;

        //Get the File Change service
        if (null == vsFileChangeEx)
            vsFileChangeEx = (IVsFileChangeEx)GetService(typeof(SVsFileChangeEx));
        if (null == vsFileChangeEx)
            return VSConstants.E_UNEXPECTED;

        // Setup Notification if startNotify is TRUE, Remove if startNotify is FALSE.
        if (startNotify)
        {
            if (vsFileChangeCookie == VSConstants.VSCOOKIE_NIL)
            {
                //Receive notifications if either the attributes of the file change or 
                //if the size of the file changes or if the last modified time of the file changes
                result = vsFileChangeEx.AdviseFileChange(fileNameToNotify,
                    (uint)(_VSFILECHANGEFLAGS.VSFILECHG_Attr | _VSFILECHANGEFLAGS.VSFILECHG_Size | _VSFILECHANGEFLAGS.VSFILECHG_Time),
                    this,
                    out vsFileChangeCookie);
                if (vsFileChangeCookie == VSConstants.VSCOOKIE_NIL)
                {
                    return VSConstants.E_FAIL;
                }
            }
            result = VSConstants.S_OK;
        }
        else
        {
            if (vsFileChangeCookie != VSConstants.VSCOOKIE_NIL)
            {
                //if we want to unadvise and the cookieTextViewEvents isnt null then unadvise changes
                result = vsFileChangeEx.UnadviseFileChange(vsFileChangeCookie);
                vsFileChangeCookie = VSConstants.VSCOOKIE_NIL;
                result = VSConstants.S_OK;
            }
        }
        return result;
    }
}