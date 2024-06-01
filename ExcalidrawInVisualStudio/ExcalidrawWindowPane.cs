﻿using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using System.Windows.Input;
using EnvDTE;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Debugger = System.Diagnostics.Debugger;

namespace ExcalidrawInVisualStudio;

[Guid("55415F2D-3595-4DA8-87DF-3F9388DAD6C2")]
public class ExcalidrawWindowPane : 
    WindowPane,
    IVsPersistDocData,
    IVsFileChangeEvents,
    IVsDocDataFileChangeControl
{
    // I'm making this timeout long because Visual Studio can be quite busy loading a large project and if it's
    // re-loading an excalidraw file we may be hanging around for a while. We're not blocking the Visual Studio
    // thread so waiting for a while is fine.
    private const int WaitForWebViewTimeOutInSeconds = 30;

    private readonly WebView2 _webView = new() { HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch };
    private string _filename;
    private bool _isDisposed;
    private bool _isDirty;
    private IVsUIShell _uiShell;

    // Counter of the file system changes to ignore.
    private int _changesToIgnore;
    private IVsFileChangeEx _vsFileChangeEx;
    private Timer _reloadTimer = new();
    private bool _fileChangedTimerSet;

    // Cookie for the subscription to the file system notification events.
    private uint _vsFileChangeCookie;

    private readonly TaskCompletionSource<bool> _webViewInitialisedTaskSource = new(false);

    private readonly Dictionary<string, string> _commandMappings = new();

    public ExcalidrawWindowPane() : base(null)
    {
        base.Initialize();
        _webView.Initialized += WebView_Initialized;        
    }

    private void WebView_Initialized(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _uiShell = (IVsUIShell)GetService(typeof(SVsUIShell));
       
        CreateCommandBinding("File.SaveSelectedItems");
        CreateCommandBinding("File.SaveAll");        

        _webView.Initialized -= WebView_Initialized;
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Assembly.GetExecutingAssembly().GetName().Name);
            var webView2Environment = await CoreWebView2Environment.CreateAsync(null, tempDir);
            await _webView.EnsureCoreWebView2Async(webView2Environment);

            _webView.KeyDown += WebView_KeyDown; 
            _webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping("excalidraw-editor-host", Path.Combine(GetFolder(), "editor"), CoreWebView2HostResourceAccessKind.Allow);
            
            if (Debugger.IsAttached)
            {
                _webView.CoreWebView2.OpenDevToolsWindow();
            }

            var indexHtmlPath = Path.Combine(GetFolder(), "editor", "index.html");
            var indexHtmlContent = File.ReadAllText(indexHtmlPath);
            indexHtmlContent = indexHtmlContent
                .Replace("<!--replace-with-web-view-base-url-->", "<base href=\"http://excalidraw-editor-host/\" />")
                .Replace("replace-with-export-source", GetMarketplaceUrl());
            

            VSColorTheme.ThemeChanged += VSColorTheme_ThemeChanged;
            indexHtmlContent = indexHtmlContent.Replace("replace-with-theme", GetTheme());

            _webView.NavigateToString(indexHtmlContent);
        }).FileAndForget("excalidraw");
    }

    private void CreateCommandBinding(string commandName)
    {
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

        var scopeIndex = binding.IndexOf("::");
        if (scopeIndex > 0)
        {
            binding = binding.Substring(scopeIndex + 2);
        }
        _commandMappings.Add(binding, commandName);
    }

    private void WebView_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
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

        if (_commandMappings.TryGetValue(binding, out string commandName))
        {
            var dte = (DTE)GetService(typeof(DTE));
            dte.ExecuteCommand(commandName);
        }
    }

    private static string GetMarketplaceUrl() => $"https://www.vsixgallery.com/extension/{Vsix.Id}";

    private bool IsColorLight(Color clr) => 5 * clr.G + 2 * clr.R + clr.B > 8 * 128;

    private string GetTheme() => IsColorLight(VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey)) ? "light" : "dark";

    private void VSColorTheme_ThemeChanged(ThemeChangedEventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await _webView.ExecuteScriptAsync($"window.interop.setTheme(\"{GetTheme()}\")");
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
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    var libraryPath = GetLibraryPath();
                    if (File.Exists(libraryPath))
                    {
                        var libraryItemsJson = File.ReadAllText(libraryPath);
                        var libraryItems = JsonDocument.Parse(libraryItemsJson).RootElement.GetProperty("libraryItems").GetRawText();
                        await _webView.ExecuteScriptAsync($"window.interop.loadLibrary({libraryItems})");
                    }
                }).FileAndForget("excalidraw");

            }
            else if (eventType == "onLibraryChange")
            {
                var libraryItems = root.GetProperty("libraryItems").GetRawText();
                var libraryPath = GetLibraryPath();
                var libraryFolderPath = Path.GetDirectoryName(libraryPath);
                if (!Directory.Exists(libraryFolderPath))
                {
                    Directory.CreateDirectory(libraryFolderPath!);
                }
                File.WriteAllText(libraryPath, $$"""
                    {
                        "type": "excalidrawlib",
                        "version": 2,
                        "source": "{{GetMarketplaceUrl()}}",
                        "libraryItems": {{libraryItems}}
                    }
                    """);
            }
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"Excalidraw: Error in CoreWebView2_WebMessageReceived: {exception}");
        }
    }

    private static string GetLibraryPath()
    {
        var libraryPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        libraryPath = Path.Combine(libraryPath, "Excalidraw", "library.excalidrawlib");
        return libraryPath;
    }

    private void LoadScene()
    {
        _isDirty = false;

        // TODO parse JSON to check it's the correct format
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            SetFileChangeNotification(_filename, false);

            // Wait for the WebView to be initialised
            await _webViewInitialisedTaskSource.Task.WithTimeout(TimeSpan.FromSeconds(WaitForWebViewTimeOutInSeconds));

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
        VSColorTheme.ThemeChanged -= VSColorTheme_ThemeChanged;
        _webView.Dispose();
        _isDisposed = true;
    }

    #region IVsPersistDocData

    public int GetGuidEditorType(out Guid pClassID)
    {
        pClassID = PackageGuids.EditorFactory;
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
        if (0 == numberOfChanges || null == filesChanged || null == typesOfChanges)
            return VSConstants.E_INVALIDARG;

        //ignore file changes if we are in that mode
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
            if (0 != (typesOfChanges[i] & (int)(_VSFILECHANGEFLAGS.VSFILECHG_Time | _VSFILECHANGEFLAGS.VSFILECHG_Size)))
            {
                if (!_fileChangedTimerSet)
                {
                    _reloadTimer = new Timer();
                    _fileChangedTimerSet = true;
                    _reloadTimer.Interval = 1000;
                    _reloadTimer.Tick += OnFileChangeEvent;
                    _reloadTimer.Enabled = true;
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
            ++_changesToIgnore;
        }
        else
        {
            if (_changesToIgnore > 0)
            {
                --_changesToIgnore;
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

        //Get the File Change service
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
}