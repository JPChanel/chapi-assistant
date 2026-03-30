using System.Globalization;
using System.Text.Json;
using System.Windows.Data;

namespace Chapi.Presentation.Converters;

public class JsonArrayToDictionaryListConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var result = new List<Dictionary<string, string>>();
        if (value is not string raw || string.IsNullOrWhiteSpace(raw))
            return result;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;

                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in item.EnumerateObject())
                {
                    row[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                        JsonValueKind.Array or JsonValueKind.Object => prop.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                        _ => prop.Value.ToString()
                    };
                }

                result.Add(row);
            }
        }
        catch
        {
            // Si no es JSON válido, devolvemos lista vacía para no romper la UI.
        }

        return result;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
