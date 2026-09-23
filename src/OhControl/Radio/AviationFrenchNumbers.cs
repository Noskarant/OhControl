using System;
using System.Collections.Generic;
using System.Linq;

namespace OhControl.Radio
{
    public static class AviationFrenchNumbers
    {
        private static readonly IReadOnlyDictionary<char, string> Digits =
            new Dictionary<char, string>
            {
                ['0'] = "zéro",
                ['1'] = "un",
                ['2'] = "deux",
                ['3'] = "trois",
                ['4'] = "quatre",
                ['5'] = "cinq",
                ['6'] = "six",
                ['7'] = "sept",
                ['8'] = "huit",
                ['9'] = "neuf"
            };

        public static string DigitsOnly(int value, int minimumDigits = 0)
        {
            string raw = Math.Abs(value).ToString()
                .PadLeft(minimumDigits, '0');

            return string.Join(
                " ",
                raw.Where(char.IsDigit).Select(c => Digits[c]));
        }

        public static string Runway(string runway)
        {
            if (string.IsNullOrWhiteSpace(runway))
            {
                return "";
            }

            return string.Join(
                " ",
                runway.Where(char.IsDigit).Select(c => Digits[c]));
        }
    }
}
