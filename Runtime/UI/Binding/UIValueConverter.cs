using System;
using System.Globalization;

namespace EasyFramework.UI
{
    public static class UIValueConverter
    {
        public static bool TryConvert(object value, Type destinationType, out object converted)
        {
            converted = null;
            if (destinationType == null) return false;

            Type nullableType = Nullable.GetUnderlyingType(destinationType);
            Type actualType = nullableType ?? destinationType;
            if (value == null)
            {
                if (destinationType.IsValueType && nullableType == null) return false;
                return true;
            }

            if (actualType.IsInstanceOfType(value))
            {
                converted = value;
                return true;
            }

            try
            {
                if (actualType.IsEnum)
                {
                    converted = value is string text
                        ? Enum.Parse(actualType, text, true)
                        : Enum.ToObject(actualType, value);
                    return true;
                }

                converted = Convert.ChangeType(value, actualType, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
