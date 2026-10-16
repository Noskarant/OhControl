using System;
using NAudio.Wave;

namespace OhControl.Audio
{
    public sealed class RemotePilotAudioPlayer :
        IDisposable
    {
        private readonly object _gate =
            new object();

        private readonly BufferedWaveProvider _buffer;
        private readonly WaveOutEvent _output;

        public RemotePilotAudioPlayer(
            int deviceNumber = -1)
        {
            _buffer =
                new BufferedWaveProvider(
                    new WaveFormat(
                        8000,
                        16,
                        1))
                {
                    BufferDuration =
                        TimeSpan.FromSeconds(2),

                    DiscardOnBufferOverflow = true,
                    ReadFully = true
                };

            _output =
                new WaveOutEvent
                {
                    DeviceNumber = deviceNumber,
                    DesiredLatency = 80,
                    NumberOfBuffers = 3
                };

            _output.Init(_buffer);
            _output.Play();
        }

        public void AddMuLawChunk(byte[] encoded)
        {
            byte[] pcm =
                RadioVoiceCodec
                    .Decode8kMuLawToPcm(
                        encoded);

            if (pcm.Length == 0)
            {
                return;
            }

            lock (_gate)
            {
                _buffer.AddSamples(
                    pcm,
                    0,
                    pcm.Length);
            }
        }

        public void Reset()
        {
            lock (_gate)
            {
                _buffer.ClearBuffer();
            }
        }

        public void Dispose()
        {
            _output.Stop();
            _output.Dispose();
        }
    }
}
