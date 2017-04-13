using System.Windows.Controls;
using Microsoft.Practices.Prism.Regions;

namespace PonyMLR.Core.DatabaseSelect
{
    /// <summary>
    /// Interaction logic for DatabaseSelectView.xaml
    /// </summary>
    public partial class DatabaseSelectView : UserControl
    {
        public DatabaseSelectView(DatabaseSelectViewModel viewModel)
        {
            InitializeComponent();
            this.DataContext = viewModel;
        }
    }
}
