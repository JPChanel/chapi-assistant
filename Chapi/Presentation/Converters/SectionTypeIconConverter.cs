using System.Globalization;
using System.Windows.Data;
using Chapi.Domain.Documentation;

namespace Chapi.Presentation.Converters
{
    public class SectionTypeIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is DocSectionType type ? type switch
            {
                DocSectionType.Diagram => "◈",
                DocSectionType.Image   => "⊡",
                DocSectionType.Table   => "≡",
                _                      => "¶"
            } : "¶";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
