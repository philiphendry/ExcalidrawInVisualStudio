using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using Community.VisualStudio.Toolkit;
using Task = System.Threading.Tasks.Task;

namespace ExcalidrawInVisualStudio
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
    [Guid(PackageGuids.ExcalidrawEditorString)]

    [ProvideEditorFactory(typeof(EditorFactory), 100)]
    [ProvideEditorLogicalView(typeof(EditorFactory), VSConstants.LOGVIEWID.ProjectSpecificEditor_string, IsTrusted = true)]
    [ProvideEditorExtension(typeof(EditorFactory), Constants.FileExtension, 50)]
    [ProvideEditorExtension(typeof(EditorFactory), Constants.FileExtensionEmbeddedImage, 50)]
    [ProvideEditorExtension(typeof(EditorFactory), ".png", 1)]

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
