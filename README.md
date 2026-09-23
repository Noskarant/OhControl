# OhControl

OhControl is a VFR ATC training companion for Microsoft Flight Simulator 2024.

The first development target is **Lyon-Bron (LFLY)**. The MVP starts with a robust SimConnect telemetry layer before adding French VFR phraseology, push-to-talk, speech recognition, ATC state logic, text-to-speech and radio audio processing.

## MVP roadmap

1. Connect to MSFS 2024 through SimConnect.
2. Read live aircraft telemetry and COM1 state.
3. Model LFLY runway/circuit geometry and detect flight phases.
4. Add push-to-talk and French speech-to-text.
5. Add deterministic French VFR ATC state machine.
6. Add controller TTS and VHF-style audio processing.
7. Add readback validation and training feedback.

## Development prerequisites

- Windows x64
- Visual Studio 2022
- .NET Framework 4.8 developer pack
- Microsoft Flight Simulator 2024
- MSFS 2024 SDK with the SimConnect managed library installed

The project expects the managed SimConnect assembly at:

`$(MSFS2024_SDK)\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll`

If the `MSFS2024_SDK` environment variable is not defined, the project falls back to:

`C:\MSFS 2024 SDK`

## Status

Bootstrap phase: SimConnect telemetry prototype.
