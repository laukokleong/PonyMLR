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
using System.Data.Entity;
using PonyMLR.DataAccess;

namespace PonyMLR.Modules.Manage
{
    /// <summary>
    /// Interaction logic for ManageView.xaml
    /// </summary>
    public partial class ManageView : UserControl
    {       
        public ManageView()
        {
            InitializeComponent();
        }

        public ManageView(ManageViewModel viewmodel)
            : this()
        {
            this.Loaded += (s, e) => { this.DataContext = viewmodel; };
        }

        private void Calendar_Loaded(object sender, RoutedEventArgs e)
        {
            Calendar cal = sender as Calendar;
            cal.BlackoutDates.Add(new CalendarDateRange(DateTime.Now, DateTime.MaxValue));
        }
    }
}