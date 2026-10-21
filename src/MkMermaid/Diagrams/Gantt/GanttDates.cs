using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MkMermaid.Diagrams.Gantt;

/// <summary>Day.js-style <c>dateFormat</c> parsing, durations and d3-style <c>axisFormat</c> output.</summary>
internal static partial class GanttDates
{
    private static readonly string[] Tokens = ["YYYY", "YY", "MMMM", "MMM", "MM", "M", "DD", "D", "HH", "H", "hh", "h", "mm", "m", "ss", "s", "SSS", "A", "a", "X", "x", "Q", "Do"];

    [GeneratedRegex(@"^(?<value>\d+(?:\.\d+)?)\s*(?<unit>ms|s|m|h|d|w|M|y)$", RegexOptions.CultureInvariant)]
    private static partial Regex Duration();

    public static bool TryParse(string text, string format, out DateTime value)
    {
        value = default;
        text = text.Trim();
        if (format is "X" or "x")
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var stamp) || !double.IsFinite(stamp))
            {
                return false;
            }

            var ms = format == "X" ? stamp * 1000 : stamp;
            if (Math.Abs(ms) > 2.5e14)
            {
                return false;
            }

            value = DateTime.UnixEpoch.AddMilliseconds(ms);
            return true;
        }

        var pattern = new StringBuilder("^");
        var fields = new List<string>();
        for (var i = 0; i < format.Length;)
        {
            if (format[i] == '[')
            {
                var close = format.IndexOf(']', i);
                if (close < 0)
                {
                    return false;
                }

                pattern.Append(Regex.Escape(format[(i + 1)..close]));
                i = close + 1;
                continue;
            }

            var token = Tokens.FirstOrDefault(t => string.CompareOrdinal(format, i, t, 0, t.Length) == 0);
            if (token is null)
            {
                pattern.Append(char.IsWhiteSpace(format[i]) ? @"\s+" : Regex.Escape(format[i].ToString()));
                i++;
                continue;
            }

            fields.Add(token);
            pattern.Append(token switch
            {
                "YYYY" => @"(\d{4})",
                "SSS" => @"(\d{1,3})",
                "MMMM" or "MMM" => @"([A-Za-z]+)",
                "A" or "a" => @"([AaPp][Mm])",
                "Do" => @"(\d{1,2})(?:st|nd|rd|th)",
                "YY" or "MM" or "DD" or "HH" or "hh" or "mm" or "ss" => @"(\d{2})",
                _ => @"(\d{1,2})"
            });
            i += token.Length;
        }

        pattern.Append('$');
        var match = Regex.Match(text, pattern.ToString(), RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success)
        {
            return false;
        }

        int year = 1970, month = 1, day = 1, hour = 0, minute = 0, second = 0, millisecond = 0;
        bool? pm = null;
        for (var f = 0; f < fields.Count; f++)
        {
            var raw = match.Groups[f + 1].Value;
            _ = int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var n);
            switch (fields[f])
            {
                case "YYYY":
                    year = n;
                    break;
                case "YY":
                    year = n < 69 ? 2000 + n : 1900 + n;
                    break;
                case "MMMM" or "MMM":
                    var index = Array.FindIndex(CultureInfo.InvariantCulture.DateTimeFormat.MonthNames,
                        name => name.Length > 0 && name.StartsWith(raw, StringComparison.OrdinalIgnoreCase) && raw.Length >= 3);
                    if (index < 0)
                    {
                        return false;
                    }

                    month = index + 1;
                    break;
                case "MM" or "M":
                    month = n;
                    break;
                case "Q":
                    month = (n - 1) * 3 + 1;
                    break;
                case "DD" or "D" or "Do":
                    day = n;
                    break;
                case "HH" or "H" or "hh" or "h":
                    hour = n;
                    break;
                case "mm" or "m":
                    minute = n;
                    break;
                case "ss" or "s":
                    second = n;
                    break;
                case "SSS":
                    millisecond = n;
                    break;
                case "A" or "a":
                    pm = raw.StartsWith('p') || raw.StartsWith('P');
                    break;
            }
        }

        if (pm is { } isPm)
        {
            hour = hour % 12 + (isPm ? 12 : 0);
        }

        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(Math.Clamp(year, 1, 9999), month) || hour > 23 || minute > 59 || second > 59 || year is < 1 or > 9999)
        {
            return false;
        }

        value = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Unspecified);
        return true;
    }

    public static bool TryAddDuration(DateTime start, string text, out DateTime end)
    {
        end = start;
        var match = Duration().Match(text.Trim());
        if (!match.Success)
        {
            return false;
        }

        var amount = double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
        try
        {
            end = match.Groups["unit"].Value switch
            {
                "ms" => start.AddMilliseconds(amount),
                "s" => start.AddSeconds(amount),
                "m" => start.AddMinutes(amount),
                "h" => start.AddHours(amount),
                "d" => start.AddDays(amount),
                "w" => start.AddDays(amount * 7),
                "M" => start.AddMonths((int)Math.Min(amount, 120000)),
                _ => start.AddYears((int)Math.Min(amount, 9999))
            };
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>Formats a date with the d3-time-format directives Mermaid uses for <c>axisFormat</c>.</summary>
    public static string Format(DateTime date, string format)
    {
        var culture = CultureInfo.InvariantCulture;
        var result = new StringBuilder();
        for (var i = 0; i < format.Length; i++)
        {
            if (format[i] != '%' || i + 1 >= format.Length)
            {
                result.Append(format[i]);
                continue;
            }

            var c = format[++i];
            var pad = true;
            if (c is '-' or '_' or '0' && i + 1 < format.Length)
            {
                pad = c != '-';
                c = format[++i];
            }

            string Two(int n) => pad ? n.ToString("00", culture) : n.ToString(culture);
            result.Append(c switch
            {
                'Y' => date.Year.ToString(culture),
                'y' => Two(date.Year % 100),
                'm' => Two(date.Month),
                'd' => Two(date.Day),
                'e' => date.Day.ToString(culture).PadLeft(pad ? 2 : 1),
                'H' => Two(date.Hour),
                'I' => Two(date.Hour % 12 == 0 ? 12 : date.Hour % 12),
                'M' => Two(date.Minute),
                'S' => Two(date.Second),
                'L' => date.Millisecond.ToString("000", culture),
                'p' => date.Hour < 12 ? "AM" : "PM",
                'a' => culture.DateTimeFormat.GetAbbreviatedDayName(date.DayOfWeek),
                'A' => culture.DateTimeFormat.GetDayName(date.DayOfWeek),
                'b' or 'h' => culture.DateTimeFormat.GetAbbreviatedMonthName(date.Month),
                'B' => culture.DateTimeFormat.GetMonthName(date.Month),
                'j' => date.DayOfYear.ToString(pad ? "000" : "0", culture),
                'q' => ((date.Month - 1) / 3 + 1).ToString(culture),
                'u' => (date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek).ToString(culture),
                'w' => ((int)date.DayOfWeek).ToString(culture),
                'V' => Two(ISOWeek.GetWeekOfYear(date)),
                'U' => Two((date.DayOfYear + 6 - (int)date.DayOfWeek) / 7),
                'W' => Two((date.DayOfYear + 6 - ((int)date.DayOfWeek + 6) % 7) / 7),
                'Z' or 'z' => "+0000",
                '%' => "%",
                _ => "%" + c
            });
        }

        return result.ToString();
    }
}
