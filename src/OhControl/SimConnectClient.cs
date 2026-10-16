using System;
using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;

namespace OhControl
{
    public sealed class SimConnectClient : IDisposable
    {
        public const int WindowMessageId = 0x0402;

        private SimConnect _simConnect;

        private enum DataDefinitions
        {
            AircraftTelemetry
        }

        private enum DataRequests
        {
            AircraftTelemetry
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        private struct AircraftTelemetryData
        {
            public double LatitudeDeg;
            public double LongitudeDeg;
            public double AltitudeFt;
            public double HeadingMagneticDeg;
            public double IndicatedAirspeedKt;
            public double GroundSpeedKt;
            public double IsOnGround;
            public double Com1ActiveMhz;
            public double Com1StandbyMhz;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string Com1ActiveIdent;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string Com1ActiveType;

            public double Com1ActiveDistanceMeters;
            public double Com1Receive;
            public double Com1Transmit;
            public double WindDirectionTrueDeg;
            public double WindSpeedKt;
            public double AmbientTemperatureC;
            public double VisibilityMeters;
            public double SeaLevelPressureMb;
        }

        public bool IsConnected { get; private set; }

        public event Action Connected;
        public event Action Disconnected;
        public event Action<TelemetrySnapshot> TelemetryReceived;
        public event Action<string> Error;

        public void Connect(IntPtr windowHandle)
        {
            if (_simConnect != null)
            {
                return;
            }

            try
            {
                _simConnect = new SimConnect(
                    "OhControl",
                    windowHandle,
                    WindowMessageId,
                    null,
                    0);

                _simConnect.OnRecvOpen += OnRecvOpen;
                _simConnect.OnRecvQuit += OnRecvQuit;
                _simConnect.OnRecvException += OnRecvException;
                _simConnect.OnRecvSimobjectData += OnRecvSimobjectData;
            }
            catch (COMException ex)
            {
                Cleanup();
                Error?.Invoke(
                    "Impossible de se connecter à MSFS 2024 via SimConnect : " +
                    ex.Message);
            }
            catch (Exception ex)
            {
                Cleanup();
                Error?.Invoke(
                    "SimConnect n'a pas pu démarrer : " +
                    ex.Message);
            }
        }

        public void ReceiveMessage()
        {
            if (_simConnect == null)
            {
                return;
            }

            try
            {
                _simConnect.ReceiveMessage();
            }
            catch (COMException ex)
            {
                Error?.Invoke("SimConnect receive error: " + ex.Message);
                Disconnect();
            }
        }

        public void Disconnect()
        {
            bool wasConnected = IsConnected;
            Cleanup();

            if (wasConnected)
            {
                Disconnected?.Invoke();
            }
        }

        public void Dispose()
        {
            Disconnect();
        }

        private void OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            try
            {
                ConfigureTelemetry(sender);
                IsConnected = true;
                Connected?.Invoke();
            }
            catch (Exception ex)
            {
                Error?.Invoke("SimConnect telemetry setup failed: " + ex.Message);
                Disconnect();
            }
        }

        private void ConfigureTelemetry(SimConnect simConnect)
        {
            AddFloat(simConnect, "PLANE LATITUDE", "degrees");
            AddFloat(simConnect, "PLANE LONGITUDE", "degrees");
            AddFloat(simConnect, "PLANE ALTITUDE", "feet");
            AddFloat(simConnect, "PLANE HEADING DEGREES MAGNETIC", "degrees");
            AddFloat(simConnect, "AIRSPEED INDICATED", "knots");
            AddFloat(simConnect, "GROUND VELOCITY", "knots");
            AddFloat(simConnect, "SIM ON GROUND", "Bool");
            AddFloat(simConnect, "COM ACTIVE FREQUENCY:1", "MHz");
            AddFloat(simConnect, "COM STANDBY FREQUENCY:1", "MHz");

            simConnect.AddToDataDefinition(
                DataDefinitions.AircraftTelemetry,
                "COM ACTIVE FREQ IDENT:1",
                null,
                SIMCONNECT_DATATYPE.STRING64,
                0.0f,
                SimConnect.SIMCONNECT_UNUSED);

            simConnect.AddToDataDefinition(
                DataDefinitions.AircraftTelemetry,
                "COM ACTIVE FREQ TYPE:1",
                null,
                SIMCONNECT_DATATYPE.STRING64,
                0.0f,
                SimConnect.SIMCONNECT_UNUSED);

            AddFloat(simConnect, "COM ACTIVE DISTANCE:1", "meters");
            AddFloat(simConnect, "COM RECEIVE:1", "Bool");
            AddFloat(simConnect, "COM TRANSMIT:1", "Bool");

            AddFloat(simConnect, "AMBIENT WIND DIRECTION", "degrees");
            AddFloat(simConnect, "AMBIENT WIND VELOCITY", "knots");
            AddFloat(simConnect, "AMBIENT TEMPERATURE", "celsius");
            AddFloat(simConnect, "AMBIENT VISIBILITY", "meters");
            AddFloat(simConnect, "SEA LEVEL PRESSURE", "millibars");

            simConnect.RegisterDataDefineStruct<AircraftTelemetryData>(
                DataDefinitions.AircraftTelemetry);

            simConnect.RequestDataOnSimObject(
                DataRequests.AircraftTelemetry,
                DataDefinitions.AircraftTelemetry,
                SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SIM_FRAME,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0,
                0,
                0);
        }

        private static void AddFloat(
            SimConnect simConnect,
            string name,
            string unit)
        {
            simConnect.AddToDataDefinition(
                DataDefinitions.AircraftTelemetry,
                name,
                unit,
                SIMCONNECT_DATATYPE.FLOAT64,
                0.0f,
                SimConnect.SIMCONNECT_UNUSED);
        }

        private void OnRecvSimobjectData(
            SimConnect sender,
            SIMCONNECT_RECV_SIMOBJECT_DATA data)
        {
            if ((DataRequests)data.dwRequestID != DataRequests.AircraftTelemetry ||
                data.dwData == null ||
                data.dwData.Length == 0)
            {
                return;
            }

            var raw = (AircraftTelemetryData)data.dwData[0];

            TelemetryReceived?.Invoke(new TelemetrySnapshot
            {
                LatitudeDeg = raw.LatitudeDeg,
                LongitudeDeg = raw.LongitudeDeg,
                AltitudeFt = raw.AltitudeFt,
                HeadingMagneticDeg = NormalizeHeading(raw.HeadingMagneticDeg),
                IndicatedAirspeedKt = raw.IndicatedAirspeedKt,
                GroundSpeedKt = raw.GroundSpeedKt,
                IsOnGround = raw.IsOnGround > 0.5,
                Com1ActiveMhz = raw.Com1ActiveMhz,
                Com1StandbyMhz = raw.Com1StandbyMhz,
                Com1ActiveIdent = raw.Com1ActiveIdent ?? "",
                Com1ActiveType = raw.Com1ActiveType ?? "",
                Com1ActiveDistanceMeters = raw.Com1ActiveDistanceMeters,
                Com1Receive = raw.Com1Receive > 0.5,
                Com1Transmit = raw.Com1Transmit > 0.5,
                WindDirectionTrueDeg = NormalizeHeading(raw.WindDirectionTrueDeg),
                WindSpeedKt = raw.WindSpeedKt,
                AmbientTemperatureC = raw.AmbientTemperatureC,
                VisibilityMeters = raw.VisibilityMeters,
                SeaLevelPressureMb = raw.SeaLevelPressureMb
            });
        }

        private void OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data)
        {
            bool wasConnected = IsConnected;
            Cleanup();

            if (wasConnected)
            {
                Disconnected?.Invoke();
            }
        }

        private void OnRecvException(
            SimConnect sender,
            SIMCONNECT_RECV_EXCEPTION data)
        {
            Error?.Invoke(
                "SimConnect exception " + data.dwException +
                " (send ID " + data.dwSendID + ").");
        }

        private void Cleanup()
        {
            IsConnected = false;

            if (_simConnect == null)
            {
                return;
            }

            try
            {
                _simConnect.Dispose();
            }
            finally
            {
                _simConnect = null;
            }
        }

        private static double NormalizeHeading(double degrees)
        {
            double normalized = degrees % 360.0;
            return normalized < 0 ? normalized + 360.0 : normalized;
        }
    }
}
