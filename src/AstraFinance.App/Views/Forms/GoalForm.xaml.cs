using System.Windows.Controls;

namespace AstraFinance.App.Views.Forms;

public partial class GoalForm : UserControl
{
    public GoalForm()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => NameBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }
}
