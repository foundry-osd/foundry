using System.Globalization;
using System.Windows.Data;
using Foundry.Services.WinPe;

namespace Foundry.Converters;

public sealed class PartitionStyleAvailabilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
        {
            return true;
        }

        if (values[0] is not WinPeArchitecture architecture ||
            values[1] is not UsbPartitionStyle partitionStyle)
        {
            return true;
        }

        return architecture != WinPeArchitecture.Arm64 || partitionStyle != UsbPartitionStyle.Mbr;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
