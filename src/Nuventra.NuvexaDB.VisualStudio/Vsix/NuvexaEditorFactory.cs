#if VSSDK
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Nuventra.NuvexaDB.VisualStudio;

[Guid(NuvexaGuids.EditorFactoryString)]
public sealed class NuvexaEditorFactory : IVsEditorFactory
{
    private readonly NuvexaPackage _package;

    public NuvexaEditorFactory(NuvexaPackage package) => _package = package;

    public int SetSite(Microsoft.VisualStudio.OLE.Interop.IServiceProvider psp) => VSConstants.S_OK;

    public int MapLogicalView(ref Guid rguidLogicalView, out string? pbstrPhysicalView)
    {
        pbstrPhysicalView = null;
        return rguidLogicalView == VSConstants.LOGVIEWID.Primary_guid
            ? VSConstants.S_OK
            : VSConstants.E_NOTIMPL;
    }

    public int Close() => VSConstants.S_OK;

    public int CreateEditorInstance(
        uint grfCreateDoc,
        string pszMkDocument,
        string pszPhysicalView,
        IVsHierarchy pvHier,
        uint itemid,
        IntPtr punkDocDataExisting,
        out IntPtr ppunkDocView,
        out IntPtr ppunkDocData,
        out string pbstrEditorCaption,
        out Guid pguidCmdUI,
        out int pgrfCDW)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var pane = new NuvexaToolWindowPane(_package.Host);
        _package.JoinableTaskFactory.RunAsync(async () =>
        {
            await _package.Host.OpenOrPromptAsync(pszMkDocument, prompt =>
                Task.FromResult(NuvexaKeyDialog.Ask(prompt))).ConfigureAwait(true);
            if (pane.Content is NuvexaVsControl control)
            {
                control.Reload();
            }
        });

        ppunkDocView = Marshal.GetIUnknownForObject(pane);
        ppunkDocData = Marshal.GetIUnknownForObject(pane);
        pbstrEditorCaption = "NuvexaDB";
        pguidCmdUI = Guid.Parse(NuvexaGuids.EditorFactoryString);
        pgrfCDW = 0;
        return VSConstants.S_OK;
    }
}
#endif
