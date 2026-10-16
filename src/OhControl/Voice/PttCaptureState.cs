using System.Threading;

namespace OhControl.Voice
{
    // Atomic capture lifecycle shared by keyboard and joystick PTT callbacks.
    // State 0: idle; 1: recording; 2: stopping (WAV not yet finalized).
    public sealed class PttCaptureState
    {
        private int _state;

        public bool TryBeginCapture()
        {
            return Interlocked.CompareExchange(ref _state, 1, 0) == 0;
        }

        public bool TryBeginStop()
        {
            return Interlocked.CompareExchange(ref _state, 2, 1) == 1;
        }

        public void CompleteStop()
        {
            Interlocked.CompareExchange(ref _state, 0, 2);
        }

        public void CancelCapture()
        {
            Interlocked.CompareExchange(ref _state, 0, 1);
        }
    }
}
