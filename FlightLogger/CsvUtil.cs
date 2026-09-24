using System.Globalization;

namespace FlightLogger
{
    internal static class CsvUtil
    {
        public static string Cell(object value)
        {
            if (value == null)
                return "";

            string text;
            if (value is double d)
                text = d.ToString("G17", CultureInfo.InvariantCulture);
            else if (value is float f)
                text = f.ToString("G9", CultureInfo.InvariantCulture);
            else if (value is bool b)
                text = b ? "true" : "false";
            else
                text = value.ToString();

            if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return text;

            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
