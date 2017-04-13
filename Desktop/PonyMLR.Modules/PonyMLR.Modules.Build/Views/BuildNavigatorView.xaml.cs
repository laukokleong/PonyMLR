using System.Windows.Controls;
using Microsoft.Practices.Prism.Regions;

namespace PonyMLR.Modules.Build
{
    public sealed partial class BuildNavigatorView : UserControl
    {
        public BuildNavigatorView(BuildNavigatorViewModel viewModel)
        {
            this.InitializeComponent();
            this.DataContext = viewModel;
        }
    }
}

