using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace OhControl.Audio
{
    public sealed class RadioAudioPlayer : IDisposable
    {
        private readonly SemaphoreSlim _playLock = new SemaphoreSlim(1, 1);
        private readonly int _deviceNumber;
        private WaveOutEvent _activeOutput;

        public RadioAudioPlayer(int deviceNumber = -1)
        {
            _deviceNumber = deviceNumber;
        }

        public async Task PlayAsync(byte[] mp3Bytes, CancellationToken cancellationToken)
        {
            if (mp3Bytes == null || mp3Bytes.Length == 0)
            {
                return;
            }

            await _playLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                using (var memory = new MemoryStream(mp3Bytes, false))
                using (var reader = new Mp3FileReader(memory))
                {
                    ISampleProvider speech = new VhfRadioSampleProvider(reader.ToSampleProvider());

                    var clickIn = CreateSquelchClick(speech.WaveFormat, 28);
                    var clickOut = CreateSquelchClick(speech.WaveFormat, 18);
                    var sequence = new ConcatenatingSampleProvider(
                        new[] { clickIn, speech, clickOut });

                    var completion = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);

                    using (var output = new WaveOutEvent
                    {
                        DeviceNumber = _deviceNumber
                    })
                    using (cancellationToken.Register(() => output.Stop()))
                    {
                        _activeOutput = output;
                        output.Init(sequence.ToWaveProvider());
                        output.PlaybackStopped += (_, args) =>
                        {
                            if (args.Exception != null)
                            {
                                completion.TrySetException(args.Exception);
                            }
                            else
                            {
                                completion.TrySetResult(true);
                            }
                        };

                        output.Play();
                        await completion.Task.ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _activeOutput = null;
                _playLock.Release();
            }
        }

        public void Stop()
        {
            try
            {
                _activeOutput?.Stop();
            }
            catch
            {
                // Best effort when switching station.
            }
        }

        public void Dispose()
        {
            Stop();
            _playLock.Dispose();
        }

        private static ISampleProvider CreateSquelchClick(
            WaveFormat format,
            int milliseconds)
        {
            var noise = new SignalGenerator(format.SampleRate, format.Channels)
            {
                Type = SignalGeneratorType.White,
                Gain = 0.055
            };

            return new OffsetSampleProvider(noise)
            {
                Take = TimeSpan.FromMilliseconds(milliseconds)
            };
        }

        private sealed class VhfRadioSampleProvider : ISampleProvider
        {
            private readonly ISampleProvider _source;
            private readonly BiQuadFilter[] _highPass;
            private readonly BiQuadFilter[] _lowPass;
            private readonly Random _random = new Random();

            public VhfRadioSampleProvider(ISampleProvider source)
            {
                _source = source;
                WaveFormat = source.WaveFormat;

                _highPass = new BiQuadFilter[WaveFormat.Channels];
                _lowPass = new BiQuadFilter[WaveFormat.Channels];

                for (int channel = 0; channel < WaveFormat.Channels; channel++)
                {
                    _highPass[channel] = BiQuadFilter.HighPassFilter(
                        WaveFormat.SampleRate, 300f, 0.8f);

                    _lowPass[channel] = BiQuadFilter.LowPassFilter(
                        WaveFormat.SampleRate, 3400f, 0.8f);
                }
            }

            public WaveFormat WaveFormat { get; }

            public int Read(float[] buffer, int offset, int count)
            {
                int read = _source.Read(buffer, offset, count);

                for (int i = 0; i < read; i++)
                {
                    int channel = i % WaveFormat.Channels;
                    int index = offset + i;

                    float sample = _highPass[channel].Transform(buffer[index]);
                    sample = _lowPass[channel].Transform(sample);

                    double saturated = Math.Tanh(sample * 1.7) * 0.88;
                    double noise = (_random.NextDouble() * 2.0 - 1.0) * 0.0035;

                    buffer[index] = (float)Math.Max(
                        -1.0,
                        Math.Min(1.0, saturated + noise));
                }

                return read;
            }
        }
    }
}
