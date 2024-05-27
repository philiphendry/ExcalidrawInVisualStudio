﻿using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using Community.VisualStudio.Toolkit;
using Task = System.Threading.Tasks.Task;

namespace ExcalidrawInVisualStudio
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
    [Guid(PackageGuids.ExcalidrawEditorString)]

    [ProvideLanguageExtension(typeof(EditorFactory), Constants.FileExtension)]

    [ProvideEditorFactory(typeof(EditorFactory), 214, false, CommonPhysicalViewAttributes = (int)__VSPHYSICALVIEWATTRIBUTES.PVA_SupportsPreview, TrustLevel = __VSEDITORTRUSTLEVEL.ETL_AlwaysTrusted)]
    [ProvideEditorLogicalView(typeof(EditorFactory), VSConstants.LOGVIEWID.ProjectSpecificEditor_string, IsTrusted = true)]
    [ProvideEditorExtension(typeof(EditorFactory), Constants.FileExtension, 65536, NameResourceID = 214)]

    [ProvideFileIcon(Constants.FileExtension, "ef43980f-62d6-42ba-9f24-20ea2285663b:1")]
    [ProvideBindingPath]
    public sealed class ExcalidrawPackage : ToolkitPackage
    {
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var editorFactory = new EditorFactory(this);
            RegisterEditorFactory(editorFactory);
            ((IServiceContainer)this).AddService(typeof(EditorFactory), editorFactory, true);
        }
    }
}