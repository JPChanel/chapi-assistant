using System.Reflection;

namespace app_desktop_base.Utilities;

public static class PropertyPathHelper
{
    public static object? GetValue(object? source, string path)
    {
        if (source is null || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        object? current = source;
        foreach (var part in path.Split('.'))
        {
            if (current is null)
            {
                return null;
            }

            var property = current.GetType().GetProperty(part, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (property is null)
            {
                return null;
            }

            current = property.GetValue(current);
        }

        return current;
    }

    public static IEnumerable<PropertyInfo> GetBrowsableProperties(Type type)
    {
        return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static property => property.CanRead && IsSimpleType(property.PropertyType));
    }

    private static bool IsSimpleType(Type type)
    {
        var effectiveType = Nullable.GetUnderlyingType(type) ?? type;
        return effectiveType.IsPrimitive
            || effectiveType.IsEnum
            || effectiveType == typeof(string)
            || effectiveType == typeof(decimal)
            || effectiveType == typeof(DateTime)
            || effectiveType == typeof(DateTimeOffset)
            || effectiveType == typeof(TimeSpan)
            || effectiveType == typeof(Guid);
    }
}
