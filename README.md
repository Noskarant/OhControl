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

### LFLY aeronautical data

The prototype now includes current LFLY operational data used by the VFR trainer:

- runway 16 / 34 geometry and published circuit altitude (1500 ft AMSL / 800 ft AAL)
- runway holding points A1, A2, A3 and A4
- default full-length departure holding point A1 for RWY 16 and A4 for RWY 34
- published taxiway identifiers A1-A5, T1-T3, TA/TB/TC, TN/TN2 and TC1-TC7
- A5 marked as ATC-clearance-only
- reporting points N, S, MS, NA, SA, NW and TW with published coordinates/names
- telemetry validation when the pilot announces a reporting point

Taxi routing is deliberately conservative: until the in-sim parking/taxi geometry is validated, OhControl will not invent a detailed `via ...` route. It issues the appropriate full-length holding point and uses real taxiway/reporting-point names.

### LFLY radio services

Versioned from **AIP France AD 2 LFLY, AIRAC 2026-09**:

- BRON Tour: **118.100 MHz**
- BRON Sol: **121.705 MHz**
- BRON ATIS: **128.130 MHz**

A wrong COM1 frequency means no OhControl controller.

### French VFR phraseology

The deterministic ATC engine now covers the first useful training flow:

- first contact with Bron Sol / Bron Tour
- VFR taxi request with ATIS information
- taxi clearance to the runway holding point + QNH
- taxi readback validation
- ready at holding point and transfer to Tower
- line-up-and-wait with mandatory combined phraseology
- line-up readback validation
- take-off clearance and pilot readback as "je décolle"
- integration / arrival via a published LFLY reporting point
- downwind / base / final reports
- landing clearance and pilot readback as "j'atterris"
- touch-and-go requests and clearances
- pilot-initiated go-around / circuit re-entry
- runway vacated / transfer back to Ground
- leaving the circuit / frequency

The wording follows the current DGAC/DSNA phraseology manual where implemented. The engine remains intentionally deterministic; it does not ask a language model to invent clearances.

### Voice

- configurable global keyboard push-to-talk
- optional joystick / yoke PTT button using the Windows joystick API
- 16 kHz mono microphone capture
- ElevenLabs Scribe v2 French speech-to-text
- aviation vocabulary prompting
- Eleven v4 dialogue TTS with identical professional ATC speech direction for Tower, Ground and ATIS
- three optional service-specific Voice IDs (Tower required; Ground and ATIS fall back to Tower)
- role-specific voice/audio cache isolation and French pronunciation correction for Bron
- persistent local speech cache
- VHF-style filtering, light saturation/noise and squelch clicks
- ATIS loops automatically while tuned to 128.130
- ATIS audio is cached rather than regenerated every loop

### Direct multiplayer for two PCs (no Supabase)

OhControl now offers a private two-aircraft direct TCP session.
One PC is the **ATC host** and keeps the authoritative state of both pilots:
each aircraft's callsign, clearances, radio readbacks and traffic.
The **guest** sends pilot transmissions/position updates to the host and never
generates its own independent ATC clearances. Host-generated Eleven v4 audio
is transmitted to the guest as one shared radio transmission; both aircraft
on the same frequency hear exactly the same ATC audio.

1. On both PCs, configure different callsigns and the same room code and TCP
   port (default 47650) in **Settings > Session à deux**.
2. On the host, enable multiplayer and check **Ce PC est l’hôte ATC**.
3. On the guest, enable multiplayer, uncheck the host box, and enter the
   host's IPv4 LAN address or private VPN/Tailscale address.
4. Make sure the host's Windows Firewall allows OhControl incoming TCP on
   the chosen port for the trusted private network.
5. Start OhControl on the host first, then on the guest. Both PCs must also
   use MSFS multiplayer if they want to see one another's aircraft.

The protocol is **not encrypted**: use only your trusted LAN or a private
encrypted VPN (e.g., Tailscale). Do not expose port 47650 to the open internet.
The shared room code prevents accidental joining; it is not strong network
authentication. The host's computer must remain online for the entire session.

Telemetry is published approximately five times per second:
GPS, speed, altitude, phase, runway, heading, COM1 and active PTT.
Other aircraft count as traffic even when they are on a different frequency.
Two-pilot live speech uses 8 kHz mu-law, not ElevenLabs credits; STT remains
ElevenLabs on each transmitting PC and ATC TTS runs on the host only.

An optional legacy Supabase transport remains in the codebase but is **not**
used for the direct session. Existing locally saved settings are retained.

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

## First launch

For the current source-build test, Windows users can simply double-click:

`Lancer OhControl.cmd`

The launcher:

- detects the MSFS 2024 SDK / SimConnect installation
- explains the single Microsoft SDK step if it is missing
- finds MSBuild or the .NET build tool automatically
- builds OhControl only when needed
- launches the application

On the first application launch, a four-step wizard handles:

1. SimConnect prerequisite check
2. pilot callsign / name / aircraft
3. ElevenLabs API key + controller Voice ID
4. microphone / radio output / keyboard or joystick PTT

Supabase / multiplayer is intentionally skipped during first-run setup for now.

Microsoft's managed SimConnect wrapper is installed into the system by the MSFS SDK installer, so the SDK remains the one prerequisite that OhControl does not attempt to copy or replace.

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

The ATC engine is deterministic, host-authoritative in direct multiplayer and currently targets common VFR training flows. It does not yet implement the complete French ATC rule set, detailed LFLY taxi routing, every reporting point, conflict prediction, wake-turbulence separation, transponder assignment, or every advanced radio-propagation effect. Live pilot-to-pilot voice audio is already implemented for the shared session.

The LFLY phase detector and ATC sequencing must be validated in live MSFS flights before relying on them for PPL training.


### Tower line-up and takeoff safety

A valid readback such as “Fox Bravo Charlie, on s'aligne et on attend” leaves
ATC in line-up-and-wait. The automatic clearance is sent only after GPS/SimConnect
telemetry confirms the aircraft on the active runway centreline, aligned with
the active runway direction and at low ground speed for five continuous seconds.
Another known aircraft in final/occupying the runway prevents release.
If no SimConnect position is available, **no automatic takeoff clearance** is
issued. This is a flight-simulator training tool, not a real ATC service.
