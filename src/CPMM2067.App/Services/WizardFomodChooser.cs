using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using CPMM2067.App.ViewModels;
using CPMM2067.App.Views;
using CPMM2067.Archives.Fomod;
using CPMM2067.Frameworks.Fomod;

namespace CPMM2067.App.Services;

public sealed class WizardFomodChooser : IFomodChooser
{
    public async Task<List<FomodFile>?> ChooseAsync(string moduleConfigPath, string extractedRoot)
    {
        // Build VM on UI thread, show dialog, return selections.
        var tcs = new TaskCompletionSource<List<FomodFile>?>();
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var window = MainWindowAccessor.Get();
            if (window == null) { tcs.SetResult(null); return; }
            var plan = FomodParser.Resolve(moduleConfigPath, extractedRoot);
            var vm = new FomodWizardViewModel(plan, moduleConfigPath, extractedRoot);
            var accepted = await FomodWizardDialog.ShowAsync(window, vm);
            if (!accepted) { tcs.SetResult(null); return; }
            var files = vm.ResolveSelectedFiles(extractedRoot);
            tcs.SetResult(files);
        });
        return await tcs.Task;
    }
}
