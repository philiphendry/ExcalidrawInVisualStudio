using EnvDTE;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Threading;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Debugger = System.Diagnostics.Debugger;

namespace ExcalidrawInVisualStudio;

[Guid("55415F2D-3595-4DA8-87DF-3F9388DAD6C2")]
public class ExcalidrawWindowPane :
    WindowPane,
    IVsPersistDocData,
    IVsFileChangeEvents,
    IVsDocDataFileChangeControl,
    IPersistFileFormat
{
    // I'm making this timeout long because Visual Studio can be quite busy loading a large project and if it's
    // re-loading an excalidraw file we may be hanging around for a while. We're not blocking the Visual Studio
    // thread so waiting for a while is fine.
    private const int WaitForWebViewTimeOutInSeconds = 30;

    /// <summary>This TaskCompletionSource is used to wait for the WebView to be initialised before loading the scene.</summary>
    private readonly TaskCompletionSource<bool> _webViewInitialisedTaskSource = new(false);

    private string _filename;
    private bool _isDisposed;
    private bool _isDirty;

    // Counter of the file system changes to ignore.
    private int _changesToIgnore;
    private IVsFileChangeEx _vsFileChangeEx;
    private Timer _reloadTimer = new();
    private bool _fileChangedTimerSet;

    // Cookie for the subscription to the file system notification events.
    private uint _vsFileChangeCookie;

    // IPersistFileFormat implementation
    private const uint FormatIndex = 0;
    private const string FormatName = "Excalidraw";
    private const string FormatExtension = ".excalidraw";
    private const char endLine = (char)10;

    private readonly Dictionary<string, string> _commandMappings = new();

    private readonly ExtensionConfiguration _extensionConfiguration = new();
    private WebViewManager _webViewManager = new();

    public ExcalidrawWindowPane() : base(null)
    {
        base.Initialize();

        ThreadHelper.ThrowIfNotOnUIThread();

        VSColorTheme.ThemeChanged += VSColorTheme_ThemeChanged;

        _webViewManager.OnDirty += (_, _) => { _isDirty = true; };

        _webViewManager.OnReady += (_, _) =>
        {
            CreateCommandBinding("File.SaveSelectedItems");
            CreateCommandBinding("File.SaveAll");

            _webViewInitialisedTaskSource.SetResult(true);
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                var libraryPath = _extensionConfiguration.GetLibraryPath();
                if (File.Exists(libraryPath))
                {
                    var libraryItemsJson = File.ReadAllText(libraryPath);
                    var libraryItems = JsonDocument.Parse(libraryItemsJson).RootElement.GetProperty("libraryItems").GetRawText();
                    await _webViewManager.LoadLibraryAsync(libraryItems);
                }
            }).FileAndForget("excalidraw");
        };

        _webViewManager.OnLibraryChange += (_, args) =>
        {
            var libraryPath = _extensionConfiguration.GetLibraryPath();
            var libraryFolderPath = Path.GetDirectoryName(libraryPath);
            if (!Directory.Exists(libraryFolderPath))
            {
                Directory.CreateDirectory(libraryFolderPath!);
            }
            File.WriteAllText(libraryPath,
                $$"""
                {
                    "type": "excalidrawlib",
                    "version": 2,
                    "source": "{{Constants.MarketplaceUrl}}",
                    "libraryItems": {{args.LibraryItems}}
                }
                """);
        };

        _webViewManager.OnKeyPress += (_, args) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_commandMappings.TryGetValue(args.KeyPress, out string commandName))
            {
                var dte = (DTE)GetService(typeof(DTE));
                dte.ExecuteCommand(commandName);
            }
        };

        _webViewManager.OnSceneSave += (_, args) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (args.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllBytes(_filename, args.Data);
            }
            else
            {
                File.WriteAllText(_filename,  Encoding.UTF8.GetString(args.Data));
            }
            _isDirty = false;
            SetFileChangeNotification(_filename, true);
        };
    }

    protected override void Initialize()
    {
        Content = _webViewManager.Content;
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        VSColorTheme.ThemeChanged -= VSColorTheme_ThemeChanged;
        _webViewManager.Dispose();
        _webViewManager = null;
        _isDisposed = true;
    }

    private void CreateCommandBinding(string commandName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var dte = (DTE)GetService(typeof(DTE));
        if (dte is null)
        {
            return;
        }

        if (dte.Commands.Item(commandName).Bindings is not object[] bindings || bindings.Length <= 0)
        {
            return;
        }

        var binding = bindings.FirstOrDefault() as string;
        if (binding is null)
        {
            return;
        }

        var scopeIndex = binding.IndexOf("::", StringComparison.Ordinal);
        if (scopeIndex > 0)
        {
            binding = binding.Substring(scopeIndex + 2);
        }
        _commandMappings.Add(binding, commandName);
    }

    private void VSColorTheme_ThemeChanged(ThemeChangedEventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(_webViewManager.ThemeChangedAsync).FileAndForget("excalidraw");
    }

    private void LoadScene()
    {
        SetFileChangeNotification(_filename, false);
        _isDirty = false;

        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await _webViewInitialisedTaskSource.Task.WithTimeout(TimeSpan.FromSeconds(WaitForWebViewTimeOutInSeconds));

            if (_filename.EndsWith(Constants.FileExtensionEmbeddedImage, StringComparison.OrdinalIgnoreCase))
            {
                var sceneData = File.ReadAllBytes(_filename);
                await _webViewManager.LoadScenePngAsync(sceneData);
            }
            else
            {
                var sceneData = File.ReadAllText(_filename);
                await _webViewManager.LoadSceneJsonAsync(sceneData);
            }

            SetFileChangeNotification(_filename, true);

        }).FileAndForget("excalidraw");
    }

    #region IVsPersistDocData

    public int GetGuidEditorType(out Guid pClassId)
    {
        pClassId = PackageGuids.ExcalidrawEditor;
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
                        if (_filename.EndsWith(Constants.FileExtensionEmbeddedImage, StringComparison.OrdinalIgnoreCase))
                        {
                             await _webViewManager.SaveScenePngAsync();
                        }
                        else
                        {
                            await _webViewManager.SaveSceneJsonAsync();
                        }
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
        if (numberOfChanges == 0 || filesChanged == null || typesOfChanges == null)
            return VSConstants.E_INVALIDARG;

        // Ignore file changes if we are in that mode
        if (_changesToIgnore != 0)
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
            if ((typesOfChanges[i] & (int)(_VSFILECHANGEFLAGS.VSFILECHG_Time | _VSFILECHANGEFLAGS.VSFILECHG_Size)) ==0)
            {
                continue;
            }

            if (_fileChangedTimerSet)
            {
                continue;
            }

            _reloadTimer = new Timer();
            _fileChangedTimerSet = true;
            _reloadTimer.Interval = 1000;
            _reloadTimer.Tick += OnFileChangeEvent;
            _reloadTimer.Enabled = true;
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
            _changesToIgnore++;
        }
        else
        {
            if (_changesToIgnore > 0)
            {
                _changesToIgnore--;
            }
        }

        return VSConstants.S_OK;
    }

    #endregion

    /// <summary>
    /// This event is triggered when one of the files loaded into the environment has changed outside the
    /// codeWindowHost
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnFileChangeEvent(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _reloadTimer.Enabled = false;
        var message = _filename + Environment.NewLine + Environment.NewLine + "This file has changed outside the editor. Do you wish to reload it?";
        var title = string.Empty;
        var result = 0;
        var tempGuid = Guid.Empty;
        var uiShell = (IVsUIShell)GetService(typeof(SVsUIShell));
        uiShell?.ShowMessageBox(0,
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
        _fileChangedTimerSet = false;
    }

    /// <summary>
    /// In this function we inform the shell when we wish to receive
    /// events when our file is changed, or we inform the shell when
    /// we wish not to receive events anymore.
    /// </summary>
    /// <param name="fileNameToNotify">File name string</param>
    /// <param name="startNotify">TRUE indicates advise, FALSE indicates unadvised.</param>
    /// <returns>Result of the operation</returns>
    private void SetFileChangeNotification(string fileNameToNotify, bool startNotify)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // Get the File Change service
        _vsFileChangeEx ??= (IVsFileChangeEx)GetService(typeof(SVsFileChangeEx));
        if (null == _vsFileChangeEx) return;

        // Setup Notification if startNotify is TRUE, Remove if startNotify is FALSE.
        if (startNotify)
        {
            if (_vsFileChangeCookie == VSConstants.VSCOOKIE_NIL)
            {
                //Receive notifications if either the attributes of the file change or
                //if the size of the file changes or if the last modified time of the file changes
                _vsFileChangeEx.AdviseFileChange(fileNameToNotify,
                    (uint)(_VSFILECHANGEFLAGS.VSFILECHG_Attr | _VSFILECHANGEFLAGS.VSFILECHG_Size | _VSFILECHANGEFLAGS.VSFILECHG_Time),
                    this,
                    out _vsFileChangeCookie);
            }
        }
        else
        {
            if (_vsFileChangeCookie != VSConstants.VSCOOKIE_NIL)
            {
                // If we want to unadvise and the cookieTextViewEvents isn't null then unadvise changes
                _vsFileChangeEx.UnadviseFileChange(_vsFileChangeCookie);
                _vsFileChangeCookie = VSConstants.VSCOOKIE_NIL;
            }
        }
    }

    #region IPersistFileFormat implementation

    public int GetClassID(out Guid pClassID)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ((IPersist)this).GetClassID(out pClassID);
        return VSConstants.S_OK;
    }

    public int IsDirty(out int pfIsDirty)
    {
        pfIsDirty = _isDirty ? 1 : 0;
        return VSConstants.S_OK;
    }

    public int InitNew(uint fFileFormat)
    {
        _isDirty = false;
        return VSConstants.S_OK;
    }

    public int Load(string pszFilename, uint grfMode, int fReadOnly)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if ((pszFilename == null) &&
            ((_filename == null) || (_filename.Length == 0)))
        {
            throw new ArgumentNullException("pszFilename");
        }

        bool isReload = false;

        // If the new file name is null, then this operation is a reload
        if (pszFilename == null)
        {
            isReload = true;
        }

        // Show the wait cursor while loading the file
        IVsUIShell vsUiShell = (IVsUIShell)GetService(typeof(SVsUIShell));
        if (vsUiShell != null)
        {
            // Note: we don't want to throw or exit if this call fails, so
            // don't check the return code.
            vsUiShell.SetWaitCursor();
        }

        // Set the new file name
        if (!isReload)
        {
            // Unsubscribe from the notification of the changes in the previous file.
            _filename = pszFilename;
        }
        // Load the file
        LoadScene();
        _isDirty = false;

        // Notify the load or reload
        NotifyDocChanged();
        return VSConstants.S_OK;
    }

    public int Save(string pszFilename, int remember, uint nFormatIndex)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!string.IsNullOrEmpty(pszFilename))
        {
            _filename = pszFilename;
        }
        // Save using existing logic
        SetFileChangeNotification(_filename, false);
        if (_filename.EndsWith(Constants.FileExtensionEmbeddedImage, StringComparison.OrdinalIgnoreCase))
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await _webViewManager.SaveScenePngAsync();
                SetFileChangeNotification(_filename, true);
                _isDirty = false;
            }).FileAndForget("excalidraw");
        }
        else
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await _webViewManager.SaveSceneJsonAsync();
                SetFileChangeNotification(_filename, true);
                _isDirty = false;
            }).FileAndForget("excalidraw");
        }
        return VSConstants.S_OK;
    }

    public int SaveCompleted(string pszFilename)
    {
        _isDirty = false;
        return VSConstants.S_OK;
    }

    public int GetCurFile(out string pszFilename, out uint pnFormatIndex)
    {
        pszFilename = _filename;
        pnFormatIndex = FormatIndex;
        return VSConstants.S_OK;
    }

    public int GetFormatList(out string pbstrFormatList)
    {
        string formatList = string.Format(CultureInfo.CurrentCulture, "{0}} (*{1}){2}*{1}{2}{2}", FormatName, FormatExtension, endLine);
        pbstrFormatList = formatList;
        return VSConstants.S_OK;
    }

    #endregion

    /// <summary>
    /// Gets an instance of the RunningDocumentTable (RDT) service which manages the set of currently open
    /// documents in the environment and then notifies the client that an open document has changed.
    /// </summary>
    private void NotifyDocChanged()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // Make sure that we have a file name
        if (_filename.Length == 0)
        {
            return;
        }

        // Get a reference to the Running Document Table
        IVsRunningDocumentTable runningDocTable = (IVsRunningDocumentTable)GetService(typeof(SVsRunningDocumentTable));

        // Lock the document
        uint docCookie;
        IVsHierarchy hierarchy;
        uint itemID;
        IntPtr docData;
        int hr = runningDocTable.FindAndLockDocument(
            (uint)_VSRDTFLAGS.RDT_ReadLock,
            _filename,
            out hierarchy,
            out itemID,
            out docData,
            out docCookie
        );
        ErrorHandler.ThrowOnFailure(hr);

        // Send the notification
        hr = runningDocTable.NotifyDocumentChanged(docCookie, (uint)__VSRDTATTRIB.RDTA_DocDataReloaded);

        // Unlock the document.
        // Note that we have to unlock the document even if the previous call failed.
        runningDocTable.UnlockDocument((uint)_VSRDTFLAGS.RDT_ReadLock, docCookie);

        // Check ff the call to NotifyDocChanged failed.
        ErrorHandler.ThrowOnFailure(hr);
    }
}