﻿using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Task = System.Threading.Tasks.Task;

namespace ExcalidrawInVisualStudio
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
    [Guid(PackageGuids.guidExcalidrawInVisualStudioPackageString)]
    [ProvideLanguageExtension(typeof(ExcalidrawEditorFactory), Constants.FileExtension)]
    [ProvideEditorExtension(typeof(ExcalidrawEditorFactory), Constants.FileExtension, 1000)]
    public sealed class ExcalidrawInVisualStudioPackage : AsyncPackage
    {
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var editorFactory = new ExcalidrawEditorFactory();
            RegisterEditorFactory(editorFactory);
        }
    }
}
