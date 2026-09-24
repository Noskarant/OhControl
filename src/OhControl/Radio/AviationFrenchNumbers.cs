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

        private static readonly IReadOnlyDictionary<char, string> FrequencyDigits =
            new Dictionary<char, string>
            {
                ['0'] = "zéro",
                ['1'] = "unité",
                ['2'] = "deux",
                ['3'] = "trois",
                ['4'] = "quatre",
                ['5'] = "cinq",
                ['6'] = "six",
                ['7'] = "sept",
                ['8'] = "huit",
                ['9'] = "neuf"
            };

        private static readonly IReadOnlyDictionary<char, string> IdentifierAlphabet =
            new Dictionary<char, string>
            {
                ['A'] = "Alpha",
                ['B'] = "Bravo",
                ['C'] = "Charlie",
                ['D'] = "Delta",
                ['E'] = "Echo",
                ['F'] = "Foxtrot",
                ['G'] = "Golf",
                ['H'] = "Hotel",
                ['I'] = "India",
                ['J'] = "Juliett",
                ['K'] = "Kilo",
                ['L'] = "Lima",
                ['M'] = "Mike",
                ['N'] = "November",
                ['O'] = "Oscar",
                ['P'] = "Papa",
                ['Q'] = "Quebec",
                ['R'] = "Romeo",
                ['S'] = "Sierra",
                ['T'] = "Tango",
                ['U'] = "Uniform",
                ['V'] = "Victor",
                ['W'] = "Whiskey",
                ['X'] = "X ray",
                ['Y'] = "Yankee",
                ['Z'] = "Zulu"
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

        public static string Identifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            var words =
                value
                    .ToUpperInvariant()
                    .Where(char.IsLetterOrDigit)
                    .Select(
                        c =>
                        {
                            if (char.IsDigit(c))
                            {
                                return Digits[c];
                            }

                            return IdentifierAlphabet.TryGetValue(
                                c,
                                out string word)
                                ? word
                                : c.ToString();
                        });

            return string.Join(" ", words);
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
                    ", ",
                    parts[0]
                        .Select(
                            c => FrequencyDigits[c]));

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
                       ", ",
                       decimals.Select(
                           c => FrequencyDigits[c]));
        }
    }
}
