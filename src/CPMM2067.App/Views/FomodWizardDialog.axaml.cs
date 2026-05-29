using System.Threading.Tasks;
using Avalonia.Controls;
using CPMM2067.App.ViewModels;

namespace CPMM2067.App.Views;

public partial class FomodWizardDialog : Window
{
    public bool Accepted { get; private set; }

    public FomodWizardDialog()
    {
        InitializeComponent();
        InstallButton.Click += (_, __) => { Accepted = true; Close(); };
        CancelButton.Click += (_, __) => { Accepted = false; Close(); };
    }

    public static async Task<bool> ShowAsync(Window owner, FomodWizardViewModel vm)
    {
        var dlg = new FomodWizardDialog { DataContext = vm };
        await dlg.ShowDialog(owner);
        return dlg.Accepted;
    }
}
