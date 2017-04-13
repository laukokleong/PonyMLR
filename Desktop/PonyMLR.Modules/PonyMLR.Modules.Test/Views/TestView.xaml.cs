using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using PonyMLR.Infrastructure;

namespace PonyMLR.Modules.Test
{
    /// <summary>
    /// Interaction logic for TestView.xaml
    /// </summary>
    public partial class TestView : UserControl
    {
        public TestView()
        {
            InitializeComponent();
        }

        public TestView(TestViewModel viewmodel)
            : this()
        {
            this.Loaded += (s, e) => { this.DataContext = viewmodel; };
        }
    }
}
