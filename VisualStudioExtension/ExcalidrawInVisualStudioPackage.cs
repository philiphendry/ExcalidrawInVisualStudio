using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using EnvDTE80;
using Task = System.Threading.Tasks.Task;

namespace ExcalidrawInVisualStudio
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuids.guidExcalidrawInVisualStudioPackageString)]
    [ProvideLanguageExtension("{8B382828-6202-11D1-8870-0000F87579D2}", ".excalidraw")]
    [ProvideEditorExtension(typeof(ExcalidrawEditorFactory), ".excalidraw", 50)]
    public sealed class ExcalidrawInVisualStudioPackage : AsyncPackage, IDisposable
    {
        #region Package Members

        private ExcalidrawEditorFactory _editorFactory;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var dte = await GetServiceAsync(typeof(DTE)) as DTE2;

            _editorFactory = new ExcalidrawEditorFactory();
            RegisterEditorFactory(_editorFactory);
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
