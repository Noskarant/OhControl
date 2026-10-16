using System;
using NAudio.Codecs;

namespace OhControl.Audio
{
    public static class RadioVoiceCodec
    {
        // Input: PCM16 LE, mono, 16 kHz.
        // Output: G.711 mu-law, mono, 8 kHz.
        public static byte[] Encode16kPcmTo8kMuLaw(
            byte[] pcm16k)
        {
            if (pcm16k == null ||
                pcm16k.Length < 4)
            {
                return Array.Empty<byte>();
            }

            int sourceSamples =
                pcm16k.Length / 2;

            int outputSamples =
                sourceSamples / 2;

            var output =
                new byte[outputSamples];

            int outIndex = 0;

            for (int sampleIndex = 0;
                 sampleIndex + 1 < sourceSamples;
                 sampleIndex += 2)
            {
                short first =
                    ReadInt16(
                        pcm16k,
                        sampleIndex * 2);

                short second =
                    ReadInt16(
                        pcm16k,
                        (sampleIndex + 1) * 2);

                short averaged =
                    (short)(
                        ((int)first +
                         second) /
                        2);

                output[outIndex++] =
                    MuLawEncoder
                        .LinearToMuLawSample(
                            averaged);
            }

            return output;
        }

        public static byte[] Decode8kMuLawToPcm(
            byte[] muLaw)
        {
            if (muLaw == null ||
                muLaw.Length == 0)
            {
                return Array.Empty<byte>();
            }

            var output =
                new byte[muLaw.Length * 2];

            for (int i = 0;
                 i < muLaw.Length;
                 i++)
            {
                short sample =
                    MuLawDecoder
                        .MuLawToLinearSample(
                            muLaw[i]);

                output[i * 2] =
                    (byte)(sample & 0xff);

                output[i * 2 + 1] =
                    (byte)(
                        (sample >> 8) &
                        0xff);
            }

            return output;
        }

        private static short ReadInt16(
            byte[] buffer,
            int offset)
        {
            return (short)(
                buffer[offset] |
                (buffer[offset + 1] << 8));
        }
    }
}
