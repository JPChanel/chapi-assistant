using System;
using System.Globalization;
using System.Windows.Data;
using Chapi.Domain.Documentation;

namespace Chapi.Presentation.Converters
{
    public class TemplateTitleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DocTemplate template)
            {
                return template switch
                {
                    DocTemplate.ModeloSoftware => "MODELO DE SOFTWARE",
                    DocTemplate.DisenoSistema => "DISEÑO DEL SISTEMA DE INFORMACIÓN",
                    _ => "DOCUMENTO TÉCNICO"
                };
            }
            return "DOCUMENTO";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
