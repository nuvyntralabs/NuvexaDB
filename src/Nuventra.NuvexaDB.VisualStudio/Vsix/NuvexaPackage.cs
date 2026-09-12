#if VSSDK
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace Nuventra.NuvexaDB.VisualStudio;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("NuvexaDB", "Open encrypted .nvx files in Visual Studio.", "1.0")]
[ProvideToolWindow(typeof(NuvexaToolWindowPane))]
[ProvideEditorExtension(typeof(NuvexaEditorFactory), ".nvx", 50)]
[ProvideEditorLogicalView(typeof(NuvexaEditorFactory), "{7651a703-06e5-11d1-8ebd-00a0c90f26ea}")]
[Guid(NuvexaGuids.PackageString)]
public sealed class NuvexaPackage : AsyncPackage
{
    public NuvexaToolWindow Host { get; } = new();

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        RegisterEditorFactory(new NuvexaEditorFactory(this));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.Dispose(disposing);
    }
}
#endif
