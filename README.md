# OhControl

OhControl is a private VFR ATC training companion for Microsoft Flight Simulator 2024.

The first development target is **Lyon-Bron (LFLY)**. The current prototype already contains a complete local voice/radio chain and a SimConnect telemetry layer.

## Current voice prototype

- Global push-to-talk: **hold F12**
- 16 kHz mono microphone capture
- ElevenLabs **Scribe v2** French speech-to-text
- Aviation keyterm prompting (Lyon-Bron, callsign, DR400, QNH, circuit terms...)
- Deterministic VFR training state machine for common ground/tower exchanges
- ElevenLabs **Flash v2.5** French controller voice
- Local TTS disk cache to avoid paying again for identical transmissions
- VHF-style audio filtering (roughly 300–3400 Hz), light noise/saturation and squelch clicks
- Offline voice test mode: Tower / Ground / ATIS
- ATIS loop starts automatically when the selected station is ATIS
- Wrong LFLY frequency in MSFS = no OhControl controller

## LFLY radio data

Versioned from **AIP France AD 2 LFLY, AIRAC 2026-09**:

- BRON Tour: **118.100 MHz**
- BRON Sol: **121.705 MHz**
- BRON ATIS: **128.130 MHz**

Aeronautical data must be re-checked when the AIRAC source changes.

## ElevenLabs setup

Copy:

`ohcontrol.local.example.json`

to:

`ohcontrol.local.json`

and fill in your ElevenLabs API key and controller voice ID.

Example:

```json
{
  "ElevenLabsApiKey": "...",
  "ElevenLabsVoiceId": "...",
  "PilotCallsign": "F-GABC",
  "MicrophoneDeviceNumber": -1
}
```

`MicrophoneDeviceNumber = -1` uses the Windows default recording device.

You can alternatively set:

- `OHCONTROL_ELEVENLABS_API_KEY`
- `OHCONTROL_ELEVENLABS_VOICE_ID`
- `OHCONTROL_CALLSIGN`

The real `ohcontrol.local.json` file is ignored by Git and must never be committed.

## Testing voice without MSFS

1. Configure ElevenLabs.
2. Start OhControl.
3. Ignore the SimConnect connection error if MSFS is not running.
4. Select **Tower**, **Ground**, or **ATIS** under Offline voice test.
5. For Tower/Ground, hold **F12**, speak, then release F12.
6. OhControl transcribes the call, runs it through the training state machine, synthesizes the controller response and plays it through the radio filter.
7. Selecting ATIS starts the broadcast loop automatically.

## MSFS 2024 integration

When connected, OhControl reads:

- position / altitude / heading / speed
- ground or airborne state
- COM1 active and standby frequency
- COM1 station ident and frequency type
- COM1 receive / transmit state
- distance to the active COM station
- ambient wind
- temperature
- visibility
- sea-level pressure

The active COM1 frequency automatically selects the OhControl service.

## Development prerequisites

- Windows x64
- Visual Studio 2022
- .NET Framework 4.8 developer pack
- Microsoft Flight Simulator 2024
- MSFS 2024 SDK with the managed SimConnect library

The project expects:

`$(MSFS2024_SDK)\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll`

If `MSFS2024_SDK` is not defined, it falls back to:

`C:\MSFS 2024 SDK`

## Current limitations

The current ATC engine is deliberately deterministic and only covers a first set of common VFR calls. It does **not** yet model other traffic, detailed LFLY taxi routes, circuit geometry, real separation, conflict detection or every DGAC phraseology case.

The next stage is to feed the voice engine with precise aircraft position/flight-phase detection around LFLY.
