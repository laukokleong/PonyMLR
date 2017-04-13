using System.Windows.Controls;
using Microsoft.Practices.Prism.Regions;

namespace PonyMLR.Modules.Manage
{
    public sealed partial class ManageNavigatorView : UserControl
    {
        public ManageNavigatorView(ManageNavigatorViewModel viewModel)
        {
            this.InitializeComponent();
            this.DataContext = viewModel;
        }
    }
}
