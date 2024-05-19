﻿using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Package;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Task = System.Threading.Tasks.Task;

namespace ExcalidrawInVisualStudio
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(ExcalidrawInVisualStudioPackage.PackageGuidString)]
    [ProvideLanguageExtension("{8B382828-6202-11D1-8870-0000F87579D2}", ".excalidraw")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(ToolWindow))]
    [ProvideEditorExtension(typeof(ExcalidrawEditorFactory), ".excalidraw", 50)]
    public sealed class ExcalidrawInVisualStudioPackage : AsyncPackage, IDisposable
    {
        /// <summary>
        /// ExcalidrawInVisualStudioPackage GUID string.
        /// </summary>
        public const string PackageGuidString = "abcd570c-4efd-4029-9deb-13a2dc2cef7a";

        #region Package Members

        private ExcalidrawEditorFactory _editorFactory;

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // When initialized asynchronously, the current thread may be a background thread at this point.
            // Do any initialization that requires the UI thread after switching to the UI thread.
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // await ToolWindowCommand.InitializeAsync(this);

            _editorFactory = new ExcalidrawEditorFactory();
            RegisterEditorFactory(_editorFactory);


            //var dte = await GetServiceAsync(typeof(DTE)) as DTE2;
            //dte.Events.DocumentEvents.DocumentOpened += DocumentEvents_DocumentOpened;
        }

        private void DocumentEvents_DocumentOpened(Document document)
        {
            // Check if the document has the .excalidraw extension
            if (Path.GetExtension(document.FullName).Equals(".excalidraw", StringComparison.OrdinalIgnoreCase))
            {
                // Open the custom window
                var window = FindToolWindow(typeof(ToolWindow), 0, true);
                if ((null == window) || (null == window.Frame))
                {
                    throw new NotSupportedException("Cannot create tool window");
                }
                IVsWindowFrame windowFrame = (IVsWindowFrame)window.Frame;
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
            }
        }

        #endregion

        public void Dispose()
        {
            Dispose(true);
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                Debug.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering Dispose() of: {0}", ToString()));
                if (disposing)
                {
                    if (_editorFactory != null)
                    {
                        _editorFactory.Dispose();
                        _editorFactory = null;
                    }
                    GC.SuppressFinalize(this);
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }
    }
}
