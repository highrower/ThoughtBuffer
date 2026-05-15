# Twilio Live Media Streams

This document captures the Phase 7A design findings and Phase 7B walking skeleton for Twilio live media streaming. It is not a production streaming transcription implementation yet.

## Current Decision

Use Twilio unidirectional Media Streams for the current call-forwarding shape.

The current batch voice webhook returns TwiML that forwards the call with `<Dial>` and enables completed recording callbacks. The live streaming skeleton starts a unidirectional stream with `<Start><Stream>` and then continues to `<Dial>` only when `Twilio__EnableLiveMediaStreams=true`. Twilio documents that `<Start><Stream>` starts the WebSocket media stream and then continues with the next TwiML instruction.

Do not use `<Connect><Stream>` for the initial forwarded-call design. `<Connect><Stream>` is for bidirectional streams, blocks subsequent TwiML until the WebSocket is disconnected, and only receives inbound audio. It is better suited to an AI assistant or conversational IVR call flow where the app sends audio back to Twilio.

## Required Twilio-Side Setup

For a live-streaming smoke test, set:

```text
Twilio__EnableLiveMediaStreams=true
Twilio__LiveStreamTrack=both_tracks
Twilio__LiveStreamName=thoughtbuffer-live
Twilio__LiveStreamStoreMetadata=true
Twilio__LiveStreamStoreRawChunks=false
```

The Twilio voice webhook then returns TwiML that includes a Stream noun:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Response>
  <Start>
    <Stream
      name="thoughtbuffer-live"
      url="wss://thoughtbuffer-api-jairo-dev-62217.azurewebsites.net/api/twilio/media-stream"
      track="both_tracks">
      <Parameter name="source" value="twilio-live-media-stream" />
    </Stream>
  </Start>
  <Dial>
    <Number>+1...</Number>
  </Dial>
</Response>
```

Important Twilio constraints:

- The WebSocket URL must use `wss`.
- The `url` attribute does not support query string parameters; use nested `<Parameter>` values for custom metadata.
- For unidirectional `<Start><Stream>`, `track` can be `inbound_track`, `outbound_track`, or `both_tracks`.
- `inbound_track` is audio Twilio receives from the caller.
- `outbound_track` is audio Twilio generates to the caller, including `<Say>`, `<Play>`, or child call audio from `<Dial>`.
- `both_tracks` should be tested with the forwarded-call shape because it produces more events but may be needed to capture both parties.
- For bidirectional `<Connect><Stream>`, only inbound track is received.

## Required Azure-Side Setup

Proposed WebSocket endpoint:

```text
wss://thoughtbuffer-api-jairo-dev-62217.azurewebsites.net/api/twilio/media-stream
```

Azure App Service requirements before a live test:

1. Enable WebSockets on the App Service.
2. Confirm the App Service plan tier.
3. Confirm the API host accepts WebSocket upgrade requests.
4. Confirm App Service logs are enabled at `Information` during test calls.
5. Confirm no access restriction, proxy, or front-door layer blocks Twilio's WebSocket connection.

Azure App Service Free F1 is not a reliable production target for streaming. Microsoft documents 5 WebSocket connections per instance on Free for Windows and Linux App Service. Free and Shared tiers also run on shared compute with CPU quotas. A single spike call may work, but Phase 7B should use at least Basic for reliable testing and should not rely on F1 for acceptance.

Useful Azure commands:

```powershell
az appservice plan show `
  --name <plan-name> `
  --resource-group rg-thoughtbuffer-dev `
  --query "{name:name,sku:sku.name,tier:sku.tier}"

az webapp config set `
  --name thoughtbuffer-api-jairo-dev-62217 `
  --resource-group rg-thoughtbuffer-dev `
  --web-sockets-enabled true

az webapp log config `
  --name thoughtbuffer-api-jairo-dev-62217 `
  --resource-group rg-thoughtbuffer-dev `
  --application-logging filesystem `
  --level Information `
  --web-server-logging filesystem
```

## WebSocket Messages From Twilio

Twilio sends JSON text messages over the WebSocket.

Expected inbound message types:

- `connected`: sent once the WebSocket connection is established.
- `start`: contains stream metadata such as `streamSid`, `callSid`, tracks, custom parameters, and media format.
- `media`: contains audio metadata and a base64 audio payload.
- `stop`: sent when the stream stops or the call ends.
- `dtmf`: relevant only for bidirectional streams and inbound DTMF.
- `mark`: relevant only for bidirectional streams after the server sends media and mark messages back to Twilio.

For Phase 7B unidirectional receive-only streaming, the walking skeleton parses `connected`, `start`, `media`, and `stop`. It explicitly logs-and-ignores `dtmf`, `mark`, and unknown event types.

Twilio media format:

```text
encoding: audio/x-mulaw
sampleRate: 8000
channels: 1
payload: base64 audio bytes
```

Each media event includes:

- `streamSid`
- `sequenceNumber`
- `media.track`
- `media.chunk`
- `media.timestamp`
- `media.payload`

The app decodes the base64 payload, keeps sequence/chunk counters, and counts decoded bytes by track. It does not feed decoded audio to a streaming transcription adapter yet. Do not log payloads.

## Difference From Recording Status Callback

`recordingStatusCallback` is batch-oriented:

```text
Twilio completes recording
  -> POST /api/twilio/recording-status
  -> API downloads completed recording
  -> ProcessBatchAudioAsync
  -> final artifacts
```

Media Streams are session-oriented:

```text
Twilio starts WebSocket stream
  -> GET /api/twilio/media-stream upgrade
  -> connected/start/media/stop events over time
  -> live session state
  -> partial transcript events
  -> finalization after stop/disconnect
  -> final artifacts
```

The live path must not call `ProcessBatchAudioAsync` directly. Batch processing is for completed files; live processing is for long-lived stream sessions and chunks.

## Phase 7B Metadata Artifact

On `stop` or WebSocket close, the skeleton writes:

```text
sessions/{sessionId}/metadata/live-stream.json
```

The metadata includes:

- `sessionId`
- `streamSid`
- `callSid`
- `source=TwilioMediaStream`
- requested track
- tracks received
- media format
- chunk counts by inbound/outbound track
- decoded byte counts by inbound/outbound track
- first and last media timestamps
- stop event received flag
- start/stop timestamps
- finalization status
- failure reason, if any
- custom Twilio parameters after filtering sensitive parameter names

Raw chunks are not stored by default. Final audio is not reconstructed. Transcript and note artifacts are not created by the live skeleton.

## Security And Validation

Open design item: Twilio webhook signatures protect HTTP webhooks, but the WebSocket endpoint has a different handshake shape. Phase 7B should verify Twilio's recommended validation strategy for Media Streams. At minimum, use hard-to-guess stream names/custom parameters only as correlation aids, not authentication.

Do not put secrets in `<Parameter>` values. Twilio includes custom parameters in the `start` message.

## Observability

Required structured log fields:

- `streamSid`
- `callSid`
- `sessionId`
- event type
- track
- sequence number
- chunk number
- media timestamp
- chunk counts by track
- duplicate count
- sequence gap count
- partial transcript count
- finalization status
- failure reason

Never log:

- Twilio auth token
- base64 audio payloads
- raw audio bytes
- full transcript text
- private blob contents

## Unknowns And Deferred Decisions

- Whether `both_tracks` captures the forwarded party exactly as needed with the current `<Dial>` call shape.
- Whether the first production streaming provider expects mulaw/8000 directly or requires conversion to PCM.
- Whether final audio reconstruction is required for live sessions.
- Whether partial transcripts need durable storage before finalization.
- How to validate the WebSocket request source/signature.
- Whether live streaming should coexist with batch recording in the same TwiML for early testing.
- How to represent calls where batch recording succeeds but live stream finalization fails.
- Whether App Service should be upgraded before any live test; current docs suggest F1 is too constrained for reliable streaming.

## Sources

- Twilio Media Streams overview: https://www.twilio.com/docs/voice/media-streams
- Twilio `<Stream>` TwiML reference: https://www.twilio.com/docs/voice/twiml/stream
- Twilio Media Streams WebSocket messages: https://www.twilio.com/docs/voice/media-streams/websocket-messages
- Azure App Service WebSocket limits: https://learn.microsoft.com/en-us/azure/azure-resource-manager/management/azure-subscription-service-limits#azure-app-service-limits
- Azure WebSockets configuration guidance: https://learn.microsoft.com/en-us/aspnet/signalr/overview/deployment/using-signalr-with-azure-web-sites
