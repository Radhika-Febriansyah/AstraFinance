using System.Windows.Controls;

namespace AstraFinance.App.Views.Forms;

public partial class ChangePinForm : UserControl
{
    public ChangePinForm()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => CurrentBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }
}
