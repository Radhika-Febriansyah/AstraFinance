using System.Windows.Controls;

namespace AstraFinance.App.Views.Forms;

public partial class BudgetForm : UserControl
{
    public BudgetForm()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => AmountBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }
}
