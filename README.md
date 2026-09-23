# OhControl

OhControl is a private VFR ATC training companion for Microsoft Flight Simulator 2024.

The first development target is **Lyon-Bron (LFLY)**. The project is designed for two private pilots-in-training sharing the same virtual airspace while using the aircraft radios normally inside MSFS.

## Current prototype

### MSFS / SimConnect

OhControl reads live:

- latitude / longitude
- altitude
- magnetic heading
- IAS / ground speed
- ground / airborne state
- COM1 active / standby
- COM1 active station ident and type
- COM1 RX / TX state
- active COM station distance
- wind direction / speed
- ambient temperature
- visibility
- sea-level pressure

The active COM1 frequency automatically selects the OhControl service.

### LFLY radio services

Versioned from **AIP France AD 2 LFLY, AIRAC 2026-09**:

- BRON Tour: **118.100 MHz**
- BRON Sol: **121.705 MHz**
- BRON ATIS: **128.130 MHz**

A wrong COM1 frequency means no OhControl controller.

### Voice

- configurable global keyboard push-to-talk
- optional joystick / yoke PTT button using the Windows joystick API
- 16 kHz mono microphone capture
- ElevenLabs Scribe v2 French speech-to-text
- aviation vocabulary prompting
- ElevenLabs Flash v2.5 controller TTS
- persistent local speech cache
- VHF-style filtering, light saturation/noise and squelch clicks
- ATIS loops automatically while tuned to 128.130
- ATIS audio is cached rather than regenerated every loop

### Multiplayer for two PCs

Multiplayer uses **Supabase Realtime Broadcast** directly over WebSocket.

No database table is required.

Each client publishes about five telemetry updates per second:

- callsign and aircraft type
- position / altitude / heading / speeds
- COM1 state
- active runway
- estimated LFLY circuit phase
- runway-threshold distance
- PTT state

OhControl tracks remote players and removes stale aircraft automatically.

If both pilots use the same room code:

- both aircraft are visible to the ATC engine
- ATC can sequence one aircraft behind the other
- a departure can be held when the other aircraft is detected in final/runway state
- both clients know when the frequency is occupied
- simultaneous PTT on the same frequency is treated as a double transmission and is not understood by ATC
- live pilot microphone audio is compressed to 8 kHz G.711 mu-law and streamed to the other PC while transmitting
- pilot transcripts are also shared for visibility/debugging
- ATC responses are shared and played on both clients when they are monitoring that frequency
- ATIS information letters are derived from the same UTC half-hour slot so both clients stay synchronized

Pilot-to-pilot radio audio does not use ElevenLabs. It is transmitted as small low-bandwidth mu-law chunks over Realtime Broadcast, so only ATC speech/transcription consumes ElevenLabs credit.

### LFLY circuit awareness

The detector uses current official runway geometry:

- RWY 16 / 34
- true bearings approximately 163.39 / 343.39 degrees
- published circuit altitude **1500 ft AMSL (800 ft AAL)**
- RWY 34 published right-hand circuit

It estimates:

- parked
- taxiing
- runway
- initial climb
- crosswind
- downwind
- base
- final
- departed

This geometry is intentionally conservative and still needs live validation inside MSFS before it should be treated as training-grade.

## Settings

Use the **Settings** button in OhControl. You do not need to manually edit JSON for normal use.

It lets you configure:

- callsign
- player name
- aircraft type
- ElevenLabs API key
- ElevenLabs controller voice ID
- microphone selection
- radio/headset output selection
- keyboard PTT capture
- joystick/yoke PTT capture
- multiplayer on/off
- Supabase project URL
- Supabase publishable key
- multiplayer room code

Settings are saved to:

`ohcontrol.local.json`

This file is ignored by Git.

Never put a Supabase secret/service-role key in OhControl. Use a **publishable** client key.

Environment-variable alternatives:

- `OHCONTROL_ELEVENLABS_API_KEY`
- `OHCONTROL_ELEVENLABS_VOICE_ID`
- `OHCONTROL_CALLSIGN`
- `OHCONTROL_SUPABASE_URL`
- `OHCONTROL_SUPABASE_PUBLISHABLE_KEY`
- `OHCONTROL_ROOM`

## Supabase setup

For a private two-person setup:

1. Create or choose a Supabase project.
2. Copy its project URL.
3. Copy its **publishable** client key.
4. Put the same URL/key on both PCs.
5. Choose a reasonably long shared room code.
6. Enable multiplayer in OhControl.

The current implementation uses a public Realtime Broadcast channel and does not store flight data in Postgres. The room code should therefore be treated as a private session identifier, not as strong authentication.

Supabase Presence is intentionally not used for flight telemetry because it is not designed for high-frequency movement updates; Broadcast is.

## Voice test without MSFS

1. Configure ElevenLabs in Settings.
2. Start OhControl.
3. Ignore the SimConnect message if MSFS is not running.
4. Select Tower, Ground or ATIS under Offline voice test.
5. Hold the configured PTT, speak and release it.
6. OhControl transcribes the call, runs its ATC logic and plays the filtered controller response.

Two PCs can also test multiplayer without flying; meaningful movement awareness starts once SimConnect telemetry is available.

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

The ATC engine is deterministic and currently targets common VFR training flows. It does not yet implement the complete French ATC rule set, detailed LFLY taxi routing, every reporting point, conflict prediction, wake-turbulence separation, transponder assignment, or every advanced radio-propagation effect. Live pilot-to-pilot voice audio is already implemented for the shared session.

The LFLY phase detector and ATC sequencing must be validated in live MSFS flights before relying on them for PPL training.
