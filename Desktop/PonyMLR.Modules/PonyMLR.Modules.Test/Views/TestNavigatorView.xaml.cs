using System.Windows.Controls;
using Microsoft.Practices.Prism.Regions;

namespace PonyMLR.Modules.Test
{
    public sealed partial class TestNavigatorView : UserControl
    {
        public TestNavigatorView(TestNavigatorViewModel viewModel)
        {
            this.InitializeComponent();
            this.DataContext = viewModel;
        }
    }
}
