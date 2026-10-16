using System.Collections.Generic;
using System.Linq;

namespace OhControl.Radio
{
    public static class AviationCallsign
    {
        private static readonly IReadOnlyDictionary<char, string> Alphabet =
            new Dictionary<char, string>
            {
                ['A'] = "Alpha", ['B'] = "Bravo", ['C'] = "Charlie",
                ['D'] = "Delta", ['E'] = "Echo", ['F'] = "Fox",
                ['G'] = "Golf", ['H'] = "Hotel", ['I'] = "India",
                ['J'] = "Juliett", ['K'] = "Kilo", ['L'] = "Lima",
                ['M'] = "Mike", ['N'] = "November", ['O'] = "Oscar",
                ['P'] = "Papa", ['Q'] = "Quebec", ['R'] = "Romeo",
                ['S'] = "Sierra", ['T'] = "Tango", ['U'] = "Uniform",
                ['V'] = "Victor", ['W'] = "Whiskey", ['X'] = "X ray",
                ['Y'] = "Yankee", ['Z'] = "Zulu"
            };

        public static string ToSpeech(string callsign)
        {
            if (string.IsNullOrWhiteSpace(callsign))
            {
                return "Fox Golf Alpha Bravo Charlie";
            }

            var words = callsign
                .ToUpperInvariant()
                .Where(char.IsLetterOrDigit)
                .Select(c =>
                {
                    if (char.IsDigit(c))
                    {
                        return AviationFrenchNumbers.DigitsOnly(c - '0');
                    }

                    return Alphabet.TryGetValue(c, out string word)
                        ? word
                        : c.ToString();
                });

            return string.Join(" ", words);
        }
    }
}
