using System;
using System.Collections.Generic;
using System.Globalization;
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

        public static string DigitsOnly(
            int value,
            int minimumDigits = 0)
        {
            string raw =
                Math.Abs(value)
                    .ToString()
                    .PadLeft(
                        minimumDigits,
                        '0');

            return string.Join(
                " ",
                raw
                    .Where(char.IsDigit)
                    .Select(
                        c => Digits[c]));
        }

        public static string Runway(string runway)
        {
            if (string.IsNullOrWhiteSpace(runway))
            {
                return "";
            }

            return string.Join(
                " ",
                runway
                    .Where(char.IsDigit)
                    .Select(
                        c => Digits[c]));
        }

        public static string Frequency(
            double frequencyMhz)
        {
            string formatted =
                frequencyMhz.ToString(
                    "000.000",
                    CultureInfo.InvariantCulture);

            string[] parts =
                formatted.Split('.');

            string whole =
                string.Join(
                    " ",
                    parts[0]
                        .Select(
                            c => Digits[c]));

            string decimals =
                parts[1];

            // For channels such as 118.100, the fifth and sixth
            // digits are zero and are not spoken.
            if (decimals.Length == 3 &&
                decimals[1] == '0' &&
                decimals[2] == '0')
            {
                decimals =
                    decimals.Substring(0, 1);
            }

            return whole +
                   " décimale " +
                   string.Join(
                       " ",
                       decimals.Select(
                           c => Digits[c]));
        }
    }
}
