using System;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;

namespace OhControl.Audio
{
    public sealed class MicrophoneCapture : IDisposable
    {
        private readonly object _gate = new object();
        private readonly int _deviceNumber;

        private WaveInEvent _waveIn;
        private MemoryStream _stream;
        private WaveFileWriter _writer;
        private TaskCompletionSource<byte[]> _stopCompletion;

        public bool IsRecording { get; private set; }

        public MicrophoneCapture(int deviceNumber)
        {
            _deviceNumber = deviceNumber;
        }

        public void Start()
        {
            lock (_gate)
            {
                if (IsRecording)
                {
                    return;
                }

                _stream = new MemoryStream();
                _writer = new WaveFileWriter(
                    new IgnoreDisposeStream(_stream),
                    new WaveFormat(16000, 16, 1));

                _stopCompletion = new TaskCompletionSource<byte[]>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                _waveIn = new WaveInEvent
                {
                    DeviceNumber = _deviceNumber,
                    WaveFormat = new WaveFormat(16000, 16, 1),
                    BufferMilliseconds = 40,
                    NumberOfBuffers = 3
                };

                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;
                _waveIn.StartRecording();
                IsRecording = true;
            }
        }

        public Task<byte[]> StopAsync()
        {
            lock (_gate)
            {
                if (!IsRecording || _waveIn == null)
                {
                    return Task.FromResult(Array.Empty<byte>());
                }

                IsRecording = false;
                _waveIn.StopRecording();
                return _stopCompletion.Task;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                try
                {
                    _waveIn?.StopRecording();
                }
                catch
                {
                    // Best effort during shutdown.
                }

                Cleanup();
            }
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            lock (_gate)
            {
                _writer?.Write(e.Buffer, 0, e.BytesRecorded);
                _writer?.Flush();
            }
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            byte[] bytes;

            lock (_gate)
            {
                _writer?.Dispose();
                _writer = null;

                bytes = _stream?.ToArray() ?? Array.Empty<byte>();
                _stream?.Dispose();
                _stream = null;

                if (_waveIn != null)
                {
                    _waveIn.DataAvailable -= OnDataAvailable;
                    _waveIn.RecordingStopped -= OnRecordingStopped;
                    _waveIn.Dispose();
                    _waveIn = null;
                }
            }

            if (e.Exception != null)
            {
                _stopCompletion?.TrySetException(e.Exception);
            }
            else
            {
                _stopCompletion?.TrySetResult(bytes);
            }
        }

        private void Cleanup()
        {
            _writer?.Dispose();
            _writer = null;

            _stream?.Dispose();
            _stream = null;

            if (_waveIn != null)
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.RecordingStopped -= OnRecordingStopped;
                _waveIn.Dispose();
                _waveIn = null;
            }

            IsRecording = false;
        }
    }
}
