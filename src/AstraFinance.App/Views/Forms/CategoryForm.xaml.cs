using System.Windows.Controls;

namespace AstraFinance.App.Views.Forms;

public partial class CategoryForm : UserControl
{
    public CategoryForm()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => NameBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }
}
