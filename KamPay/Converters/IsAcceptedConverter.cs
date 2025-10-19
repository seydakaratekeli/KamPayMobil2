using System.Globalization;
using KamPay.Models;

namespace KamPay.Converters
{
    public class IsAcceptedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is ServiceRequestStatus status && status == ServiceRequestStatus.Accepted;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}