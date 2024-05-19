using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using IServiceProvider = Microsoft.VisualStudio.OLE.Interop.IServiceProvider;

namespace ExcalidrawInVisualStudio
{
    public class ExcalidrawEditorFactory : IVsEditorFactory, IDisposable
    {
        public int CreateEditorInstance(uint grfCreateDoc, string pszMkDocument, string pszPhysicalView, IVsHierarchy pvHier,
            uint itemid, IntPtr punkDocDataExisting, out IntPtr ppunkDocView, out IntPtr ppunkDocData,
            out string pbstrEditorCaption, out Guid pguidCmdUI, out int pgrfCDW)
        {
            var editor = new ExcalidrawWindowPane(pszMkDocument);
            ppunkDocView = Marshal.GetIUnknownForObject(editor);
            ppunkDocData = Marshal.GetIUnknownForObject(editor);
            pbstrEditorCaption = "Excalidraw Editor";
            pguidCmdUI = Guid.Empty;
            pgrfCDW = 0;

            return VSConstants.S_OK;
        }

        public int SetSite(IServiceProvider psp)
        {
            return VSConstants.S_OK;
        }

        public int Close()
        {
            return VSConstants.S_OK;
        }

        public int MapLogicalView(ref Guid rguidLogicalView, out string pbstrPhysicalView)
        {
            pbstrPhysicalView = null;
            return VSConstants.LOGVIEWID_Primary == rguidLogicalView ? VSConstants.S_OK : VSConstants.E_NOTIMPL;
        }

        public void Dispose()
        {
            
        }
    }
}