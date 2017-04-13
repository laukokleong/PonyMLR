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

namespace PonyMLR.Modules.Build
{
    /// <summary>
    /// Interaction logic for BuildView.xaml
    /// </summary>
    public partial class BuildView : UserControl
    {
        public BuildView()
        {
            InitializeComponent();
        }

        public BuildView(BuildViewModel viewmodel)
            : this()
        {
            this.Loaded += (s, e) => { this.DataContext = viewmodel; };
        }

        private void Calendar_Loaded(object sender, RoutedEventArgs e)
        {
            Calendar cal = sender as Calendar;
            cal.BlackoutDates.Add(new CalendarDateRange(DateTime.MinValue, Utils.ConvertTime(DateTime.Now, Globals.GMT_STANDARD_TIME).AddDays(-1)));
        }
    }
}
