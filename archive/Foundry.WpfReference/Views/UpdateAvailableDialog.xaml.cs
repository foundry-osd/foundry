using System.Windows;
using System.Windows.Documents;
using Foundry.ViewModels;

namespace Foundry.Views;

public partial class UpdateAvailableDialog : Window
{
    public UpdateAvailableDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not UpdateAvailableDialogViewModel viewModel)
        {
            ReleaseNotesViewer.Document = new FlowDocument();
            return;
        }

        ReleaseNotesViewer.Document = ReleaseNotesMarkdownDocumentBuilder.Build(
            viewModel.ReleaseNotesMarkdown,
            viewModel.OpenExternalUrl);
    }
}
