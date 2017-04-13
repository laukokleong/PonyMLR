using System.Windows.Controls;
using Microsoft.Practices.Prism.Regions;

namespace PonyMLR.Core.ResearchPanel
{
    /// <summary>
    /// Interaction logic for ResearchPanelView.xaml
    /// </summary>
    public partial class ResearchPanelView : UserControl
    {
        public ResearchPanelView(ResearchPanelViewModel viewModel)
        {
            InitializeComponent();
            this.DataContext = viewModel;
        }
    }
}
