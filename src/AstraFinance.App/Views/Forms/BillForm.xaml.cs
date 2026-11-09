using System.Windows.Controls;

namespace AstraFinance.App.Views.Forms;

public partial class BillForm : UserControl
{
    public BillForm()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => NameBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }
}
