using System.Threading.Tasks;
using Avalonia.Controls;

namespace CPMM2067.App.Views;

public enum ConfirmResult { Primary, Secondary, Cancel }

public partial class ConfirmDialog : Window
{
    public ConfirmResult Result { get; private set; } = ConfirmResult.Cancel;

    public ConfirmDialog()
    {
        InitializeComponent();

        PrimaryButton.Click += (_, __) => { Result = ConfirmResult.Primary; Close(); };
        SecondaryButton.Click += (_, __) => { Result = ConfirmResult.Secondary; Close(); };
        CancelButton.Click += (_, __) => { Result = ConfirmResult.Cancel; Close(); };
    }

    public void Configure(
        string title, string headline, string body,
        string primaryLabel, string? secondaryLabel = null, bool showCancel = true)
    {
        TitleText.Text = title;
        HeadlineText.Text = headline;
        BodyText.Text = body;
        PrimaryButton.Content = primaryLabel;
        if (secondaryLabel == null)
        {
            SecondaryButton.IsVisible = false;
        }
        else
        {
            SecondaryButton.Content = secondaryLabel;
            SecondaryButton.IsVisible = true;
        }
        CancelButton.IsVisible = showCancel;
    }

    public static async Task<ConfirmResult> ShowAsync(
        Window owner,
        string title,
        string headline,
        string body,
        string primaryLabel,
        string? secondaryLabel = null,
        bool showCancel = true)
    {
        var dlg = new ConfirmDialog();
        dlg.Configure(title, headline, body, primaryLabel, secondaryLabel, showCancel);
        await dlg.ShowDialog(owner);
        return dlg.Result;
    }

    public static Task<ConfirmResult> ShowResultAsync(
        Window owner, string title, string headline, string body)
        => ShowAsync(owner, title, headline, body, primaryLabel: "[ OK ]", secondaryLabel: null, showCancel: false);
}
