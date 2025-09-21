// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

namespace ReflectiveForms.Core.Utilities;

public static class DateUtility
{
    public static string DateTimeToDesiredString(DateTime dt)
    {
        return dt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    }
    public static bool FromDesiredStringToDateTime(string? desiredString, out DateTime result)
    {
        return DateTime.TryParseExact(desiredString, "yyyy-MM-ddTHH:mm:ss.fffZ", null, System.Globalization.DateTimeStyles.None, out result);
    }
}
