using System.Runtime.InteropServices;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Package;

namespace ExcalidrawInVisualStudio
{
    [ComVisible(true)]
    [Guid(PackageGuids.ExcalidrawEditorString)]
    public class EditorFactory : LanguageBase
    {
        public EditorFactory(object site) : base(site)
        {
        }

        public EditorFactory(Package package, Guid languageServiceId) : base(package, languageServiceId)
        {
        }

        public override int CreateEditorInstance(uint grfCreateDoc, string pszMkDocument, string pszPhysicalView, IVsHierarchy pvHier,
            uint itemid, IntPtr punkDocDataExisting, out IntPtr ppunkDocView, out IntPtr ppunkDocData,
            out string pbstrEditorCaption, out Guid pguidCmdUI, out int pgrfCDW)
        {
            var editor = new ExcalidrawWindowPane();
            ppunkDocView = Marshal.GetIUnknownForObject(editor);
            ppunkDocData = Marshal.GetIUnknownForObject(editor);
            pbstrEditorCaption = string.Empty;
            pguidCmdUI = Guid.Empty;
            pgrfCDW = 0;

            return VSConstants.S_OK;
        }

        public override string Name => Constants.LanguageName;

        public override string[] FileExtensions { get; } = [
            Constants.FileExtension,
            Constants.FileExtensionEmbeddedImage
            ];

        public override void SetDefaultPreferences(LanguagePreferences preferences)
        {
        }
    }
}