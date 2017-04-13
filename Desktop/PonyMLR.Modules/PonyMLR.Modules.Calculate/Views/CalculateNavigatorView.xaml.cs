using System.Windows.Controls;
using Microsoft.Practices.Prism.Regions;

namespace PonyMLR.Modules.Calculate
{
    public sealed partial class CalculateNavigatorView : UserControl
    {
        public CalculateNavigatorView(CalculateNavigatorViewModel viewModel)
        {
            this.InitializeComponent();
            this.DataContext = viewModel;
        }
    }
}
