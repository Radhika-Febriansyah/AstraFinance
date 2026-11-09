using System.Windows.Controls;
using System.Windows.Threading;

namespace AstraFinance.App.Views.Forms;

public partial class TransactionForm : UserControl
{
    public TransactionForm()
    {
        InitializeComponent();

        // The amount is what users type first.
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => AmountBox.Focus(), DispatcherPriority.Input);
    }
}
