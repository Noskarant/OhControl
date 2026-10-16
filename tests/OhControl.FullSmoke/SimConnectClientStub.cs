using System;

namespace OhControl
{
    public sealed class SimConnectClient : IDisposable
    {
        public const int WindowMessageId = 0x0402;

        public bool IsConnected { get; private set; }

        public event Action Connected;
        public event Action Disconnected;
        public event Action<TelemetrySnapshot> TelemetryReceived;
        public event Action<string> Error;

        public void Connect(IntPtr windowHandle)
        {
        }

        public void ReceiveMessage()
        {
        }

        public void Dispose()
        {
        }
    }
}
