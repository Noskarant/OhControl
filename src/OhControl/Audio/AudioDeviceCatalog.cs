using System.Collections.Generic;
using NAudio.Wave;

namespace OhControl.Audio
{
    public sealed class AudioDeviceInfo
    {
        public int DeviceNumber { get; set; }
        public string Name { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public static class AudioDeviceCatalog
    {
        public static IReadOnlyList<AudioDeviceInfo>
            GetInputDevices()
        {
            var result =
                new List<AudioDeviceInfo>
                {
                    new AudioDeviceInfo
                    {
                        DeviceNumber = -1,
                        Name = "Windows default input"
                    }
                };

            for (int i = 0;
                 i < WaveIn.DeviceCount;
                 i++)
            {
                WaveInCapabilities caps =
                    WaveIn.GetCapabilities(i);

                result.Add(
                    new AudioDeviceInfo
                    {
                        DeviceNumber = i,
                        Name =
                            i +
                            " · " +
                            caps.ProductName
                    });
            }

            return result;
        }

        public static IReadOnlyList<AudioDeviceInfo>
            GetOutputDevices()
        {
            var result =
                new List<AudioDeviceInfo>
                {
                    new AudioDeviceInfo
                    {
                        DeviceNumber = -1,
                        Name = "Windows default output"
                    }
                };

            for (int i = 0;
                 i < WaveOut.DeviceCount;
                 i++)
            {
                WaveOutCapabilities caps =
                    WaveOut.GetCapabilities(i);

                result.Add(
                    new AudioDeviceInfo
                    {
                        DeviceNumber = i,
                        Name =
                            i +
                            " · " +
                            caps.ProductName
                    });
            }

            return result;
        }
    }
}
