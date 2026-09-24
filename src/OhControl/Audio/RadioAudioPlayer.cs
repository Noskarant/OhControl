using System;
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
        private readonly SemaphoreSlim _playLock =
            new SemaphoreSlim(1, 1);

        private readonly int _deviceNumber;
        private WaveOutEvent _activeOutput;

        public RadioAudioPlayer(int deviceNumber = -1)
        {
            _deviceNumber = deviceNumber;
        }

        public async Task PlayAsync(
            byte[] mp3Bytes,
            CancellationToken cancellationToken,
            double signalQuality = 0.92)
        {
            if (mp3Bytes == null || mp3Bytes.Length == 0)
            {
                return;
            }

            double quality =
                Math.Max(0.25, Math.Min(1.0, signalQuality));

            await _playLock.WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                using (var memory =
                    new MemoryStream(mp3Bytes, false))
                using (var reader =
                    new Mp3FileReader(memory))
                {
                    ISampleProvider speech =
                        new VhfRadioSampleProvider(
                            reader.ToSampleProvider(),
                            quality);

                    var clickIn =
                        CreateSquelchClick(
                            speech.WaveFormat,
                            42,
                            0.095);

                    var clickOut =
                        CreateSquelchClick(
                            speech.WaveFormat,
                            30,
                            0.085);

                    var sequence =
                        new ConcatenatingSampleProvider(
                            new[]
                            {
                                clickIn,
                                speech,
                                clickOut
                            });

                    var completion =
                        new TaskCompletionSource<bool>(
                            TaskCreationOptions
                                .RunContinuationsAsynchronously);

                    using (var output =
                        new WaveOutEvent
                        {
                            DeviceNumber = _deviceNumber
                        })
                    using (cancellationToken.Register(
                        () => output.Stop()))
                    {
                        _activeOutput = output;

                        output.Init(
                            sequence.ToWaveProvider());

                        output.PlaybackStopped +=
                            (_, args) =>
                            {
                                if (args.Exception != null)
                                {
                                    completion.TrySetException(
                                        args.Exception);
                                }
                                else
                                {
                                    completion.TrySetResult(true);
                                }
                            };

                        output.Play();

                        await completion.Task
                            .ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _activeOutput = null;
                _playLock.Release();
            }
        }

        public async Task PlayTestToneAsync(
            CancellationToken cancellationToken)
        {
            await _playLock.WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                var generator =
                    new SignalGenerator(44100, 1)
                    {
                        Type =
                            SignalGeneratorType.Sin,
                        Frequency = 750,
                        Gain = 0.18
                    };

                var tone =
                    new OffsetSampleProvider(generator)
                    {
                        Take =
                            TimeSpan.FromMilliseconds(650)
                    };

                var completion =
                    new TaskCompletionSource<bool>(
                        TaskCreationOptions
                            .RunContinuationsAsynchronously);

                using (var output =
                    new WaveOutEvent
                    {
                        DeviceNumber = _deviceNumber
                    })
                using (cancellationToken.Register(
                    () => output.Stop()))
                {
                    _activeOutput = output;

                    output.Init(
                        tone.ToWaveProvider());

                    output.PlaybackStopped +=
                        (_, args) =>
                        {
                            if (args.Exception != null)
                            {
                                completion.TrySetException(
                                    args.Exception);
                            }
                            else
                            {
                                completion.TrySetResult(true);
                            }
                        };

                    output.Play();

                    await completion.Task
                        .ConfigureAwait(false);
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
            int milliseconds,
            double gain)
        {
            var noise =
                new SignalGenerator(
                    format.SampleRate,
                    format.Channels)
                {
                    Type = SignalGeneratorType.White,
                    Gain = gain
                };

            return new OffsetSampleProvider(noise)
            {
                Take =
                    TimeSpan.FromMilliseconds(
                        milliseconds)
            };
        }

        private sealed class VhfRadioSampleProvider :
            ISampleProvider
        {
            private readonly ISampleProvider _source;
            private readonly BiQuadFilter[] _highPass;
            private readonly BiQuadFilter[] _lowPass;
            private readonly Random _random =
                new Random();

            private readonly double _quality;
            private long _sampleCounter;
            private double _burstEnvelope;

            public VhfRadioSampleProvider(
                ISampleProvider source,
                double quality)
            {
                _source = source;
                _quality =
                    Math.Max(
                        0.25,
                        Math.Min(1.0, quality));

                WaveFormat = source.WaveFormat;

                _highPass =
                    new BiQuadFilter[
                        WaveFormat.Channels];

                _lowPass =
                    new BiQuadFilter[
                        WaveFormat.Channels];

                float highPassHz =
                    (float)(
                        340.0 +
                        (1.0 - _quality) * 120.0);

                float lowPassHz =
                    (float)(
                        3050.0 -
                        (1.0 - _quality) * 650.0);

                for (int channel = 0;
                     channel < WaveFormat.Channels;
                     channel++)
                {
                    _highPass[channel] =
                        BiQuadFilter.HighPassFilter(
                            WaveFormat.SampleRate,
                            highPassHz,
                            0.82f);

                    _lowPass[channel] =
                        BiQuadFilter.LowPassFilter(
                            WaveFormat.SampleRate,
                            lowPassHz,
                            0.82f);
                }
            }

            public WaveFormat WaveFormat { get; }

            public int Read(
                float[] buffer,
                int offset,
                int count)
            {
                int read =
                    _source.Read(
                        buffer,
                        offset,
                        count);

                double degradation =
                    1.0 - _quality;

                double drive =
                    2.15 +
                    degradation * 1.15;

                double hissLevel =
                    0.0065 +
                    degradation * 0.018;

                double crackleChance =
                    0.000025 +
                    degradation * 0.00016;

                for (int i = 0; i < read; i++)
                {
                    int channel =
                        i % WaveFormat.Channels;

                    int index =
                        offset + i;

                    float sample =
                        _highPass[channel]
                            .Transform(
                                buffer[index]);

                    sample =
                        _lowPass[channel]
                            .Transform(sample);

                    double time =
                        (double)_sampleCounter /
                        WaveFormat.SampleRate;

                    double slowFlutter =
                        1.0 +
                        0.018 *
                        Math.Sin(
                            2.0 *
                            Math.PI *
                            6.2 *
                            time) +
                        degradation *
                        0.035 *
                        Math.Sin(
                            2.0 *
                            Math.PI *
                            11.7 *
                            time);

                    double compressed =
                        Math.Tanh(
                            sample *
                            drive *
                            slowFlutter) *
                        0.90;

                    double hiss =
                        (_random.NextDouble() *
                         2.0 -
                         1.0) *
                        hissLevel;

                    if (_burstEnvelope <= 0.0001 &&
                        _random.NextDouble() <
                        crackleChance)
                    {
                        _burstEnvelope =
                            0.05 +
                            _random.NextDouble() *
                            (0.08 +
                             degradation * 0.16);
                    }

                    double crackle =
                        (_random.NextDouble() *
                         2.0 -
                         1.0) *
                        _burstEnvelope;

                    _burstEnvelope *= 0.965;

                    double result =
                        compressed +
                        hiss +
                        crackle;

                    buffer[index] =
                        (float)Math.Max(
                            -1.0,
                            Math.Min(
                                1.0,
                                result));

                    _sampleCounter++;
                }

                return read;
            }
        }
    }
}
