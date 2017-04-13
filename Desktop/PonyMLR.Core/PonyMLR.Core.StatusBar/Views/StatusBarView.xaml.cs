using System.Windows.Controls;
using Microsoft.Practices.Prism.Regions;

namespace PonyMLR.Core.StatusBar
{
    /// <summary>
    /// Interaction logic for StatusBarView.xaml
    /// </summary>
    public partial class StatusBarView : UserControl
    {
        public StatusBarView(StatusBarViewModel viewModel)
        {
            InitializeComponent();
            this.DataContext = viewModel;
        }
    }
}
