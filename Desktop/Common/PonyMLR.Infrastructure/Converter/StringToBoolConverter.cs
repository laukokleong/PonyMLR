using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Globalization;

namespace PonyMLR.Infrastructure
{
    [ValueConversion(typeof(string), typeof(bool))]
    public class StringToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string str = value as string;
            if ((str == Globals.BET_CRITERIA_SELECT_ALL) || (str == Globals.TRIGGER_MLR_SD))
                return false;

            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
