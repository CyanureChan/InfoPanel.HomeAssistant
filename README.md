# InfoPanel.HomeAssistant

Home Assistant integration for [InfoPanel](https://github.com/hongyinghsl/InfoPanel) **1.4+**. **v0.6.0** adds configurable poll interval and WebSocket live updates.

## Features

- Connect via Long-Lived Access Token (REST + WebSocket registry)
- **Entity Include** + **Entity Domains** combined (union)
- **Entity Exclude** — absolute; always overrides includes at every tier
- **Tiered cap**: entity/device/integration rules never capped; domain pool capped unless `*` or Max Entities = 0
- Comma-separated rules (InfoPanel UI friendly) with optional quoting
- **Integration.Device containers** — e.g. `Bambu Lab.A1MINI`, `Backup.Backup`
- Poll interval and update mode configurable in Plugins UI
- **WebSocket** (default): live HA push; poll interval refreshes InfoPanel only
- **HttpPoll**: full REST fetch each interval (legacy)
- **Hybrid**: WebSocket plus per-entity REST sync each interval
- Connection status in **System** container

## Home Assistant concepts

| Term | In Home Assistant | In this plugin |
|------|-------------------|----------------|
| **Domain** | Entity type prefix (`sensor`, `light`, `input_boolean`) | `domain.sensor` rule or Entity Domains list |
| **Entity** | Full id (`sensor.temperature`, `light.room`) | `entity.sensor.temperature` or shorthand `light.room` |
| **Integration** | Platform that created the entity (`mqtt`, `backup`, `bambu_lab`) | `integration.backup` rule; also the first part of container names |
| **Device** | Physical or logical device grouping entities | `device."Front Door"` rule; container name is `{Integration}.{DeviceName}` |

Container names come from the HA device registry when available. Example: the backup integration’s device appears as **Backup.Backup** — exclude it with `integration.backup`.

## Quick Start

### 1. Create a Home Assistant token

1. Open Home Assistant → your profile → **Security**
2. Create a **Long-Lived Access Token**

### 2. Install

```bash
dotnet build InfoPanel.HomeAssistant/InfoPanel.HomeAssistant/InfoPanel.HomeAssistant.csproj -c Release
```

Copy Release output to:

```
%ProgramData%\InfoPanel\plugins\InfoPanel.HomeAssistant\
```

Restart InfoPanel, then configure under **Plugins → Home Assistant**.

### 3. Configure (Plugins UI)

| Setting | Example |
|---------|---------|
| Base URL | `https://homeassistant.example.com` |
| Access Token | Your long-lived token |
| Entity Include | `*` or `device."My Printer", integration.bambu_lab` |
| Entity Domains | `sensor,climate,binary_sensor` or `*` |
| Entity Exclude | `light.room, domain.input_boolean, integration.backup` |
| Max Entities | `50` (pool cap only; use `0` or `*` for unlimited) |
| Update Mode | `WebSocket` (default), `HttpPoll`, or `Hybrid` |
| Poll Interval | `15` seconds (3–300; WebSocket mode = InfoPanel refresh rate) |

Click **Reload** after changing entity filters.

## Selection model

```text
Final set = (Include explicit + Include domains + EntityDomains) − Exclude
```

**Exclusions are absolute.** If you include all lights (`domain.light` or `*`) and exclude `light.room`, that entity is never listed — even when matched by an explicit device or integration rule.

| Tier | Rules | Cap |
|------|-------|-----|
| **Explicit** | `entity.*`, `device.*`, `integration.*` | Never capped |
| **Domain pool** | `EntityDomains`, `domain.*`, `*` in Include | Capped at **Max Entities** unless `*` or Max Entities = **0** |

Example: `EntityDomains=sensor` + `device."My Printer"` + `MaxEntities=50` → all printer entities + up to 50 sensors.

Example: `EntityInclude=*` + `EntityExclude=domain.input_boolean, integration.backup` → all supported entities except Input Booleans and the Backup integration.

Connection status example: `OK (73 entities: 23 explicit + 50 from domains, 5 devices, WebSocket, 15s, WS connected)`

## Update modes

| Mode | HA traffic | Poll interval controls |
|------|------------|------------------------|
| **WebSocket** (default) | Live `state_changed` push; per-entity REST only if WS is down | How often InfoPanel refreshes displayed values |
| **HttpPoll** | Full `GET /api/states` each interval | REST fetch frequency (legacy v0.5 behavior) |
| **Hybrid** | WebSocket push + per-entity REST sync each interval | REST sync frequency |

For large installs, prefer **WebSocket** — it avoids downloading every HA entity on each poll. Use a lower poll interval (e.g. 3–5s) for snappier InfoPanel updates while WS handles HA efficiently.

## Rule syntax

Rules are **comma-separated** (works in InfoPanel's single-line config field). Newlines also work when editing the JSON config file directly.

| Rule | Tier | Meaning |
|------|------|---------|
| `entity.sensor.temperature` | Explicit | Exact entity |
| `light.room` | Explicit | Shorthand for exact entity id |
| `device.My Printer` | Explicit | All entities on device (spaces OK) |
| `device."My Printer"` | Explicit | Quoted device name (use if name contains commas) |
| `device.<uuid>` | Explicit | Device by HA registry id |
| `integration.bambu_lab` | Explicit | All entities from integration |
| `domain.sensor` | Pool | All entities in domain |
| `*` | Pool | All supported types (uncapped) |

**Entity Exclude** uses the same syntax.

### Common exclusion examples

```text
domain.input_boolean
```

Hides all Input Boolean entities.

```text
integration.backup
```

Hides the **Backup.Backup** container and its entities.

```text
light.room
```

When including `domain.light` or `*`, removes only `light.room` while keeping other lights.

### Supported entity types

Included by `*` and domain rules:

`sensor`, `binary_sensor`, `climate`, `number`, `input_number`, `switch`, `input_boolean`, `cover`, `lock`, `light`

**Not exposed:** `automation`, `script`, `button`, `input_text`, `scene`, `group`, `update`, etc.

## Plugins tree layout

```
Home Assistant
├── System
│   └── Connection Status
├── Bambu Lab.A1MINI
│   └── sensor.bed_temperature
└── MQTT.Front Door
    └── binary_sensor.contact
```

## Binding paths

Plugin ID: `home-assistant-plugin`

| Entry | Example path |
|-------|--------------|
| Entity | `/home-assistant-plugin/bambu-lab-a1mini/sensor-bed-temperature` |
| Status | `/home-assistant-plugin/system/connection-status` |

## Development

Requires .NET 8 SDK and a local clone of the InfoPanel repository.

```
InfoPanel.HomeAssistant.Core/
  Configuration/     Settings and supported domains
  Mapping/           Grouping, slugs, state parsing
  Models/            API and discovery DTOs
  Rules/             Rule parsing (one type per file)
  Services/          API, registry, selection
InfoPanel.HomeAssistant/
  Plugin/            IPlugin entry point and engine
  Plugin/Layout/     Discovery layout types
```

Public APIs include XML documentation summaries (`GenerateDocumentationFile` enabled on Core).

Build Release and copy to `%ProgramData%\InfoPanel\plugins\InfoPanel.HomeAssistant\`, or import `InfoPanel.HomeAssistant.zip`.

## Troubleshooting

| Symptom | Check |
|---------|-------|
| Device entities missing | Use `device.*` or `integration.*` (explicit tier); domain cap does not truncate them |
| Too many domain entities | Lower Max Entities, add Entity Exclude, or use targeted domains instead of `*` |
| Excluded entity still visible | Click **Reload**; exclusion applies at discovery time |
| `device.*` matches nothing | WebSocket registry required; try device UUID |
| Reload does not update tree | Update InfoPanel to a build that includes the PluginSensors sync fix |

Logs: `%LOCALAPPDATA%\InfoPanel\logs\plugin-host-InfoPanel.HomeAssistant*.log`

## License

InfoPanel.HomeAssistant is licensed under **GPL-3.0-or-later**. See [LICENSE](LICENSE).

Copyright (C) 2026 CyanureChan
