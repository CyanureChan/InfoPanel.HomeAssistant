# InfoPanel.HomeAssistant

Home Assistant integration for [InfoPanel](https://github.com/hongyinghsl/InfoPanel) **1.4+**. **v0.6.2** uses WebSocket `subscribe_entities`, dirty-only updates, and a single configurable refresh interval.

## Features

- Connect via Long-Lived Access Token (REST + WebSocket registry)
- **Entity Include** + **Entity Domains** combined (union)
- **Entity Exclude** - absolute; always overrides includes at every tier
- **Tiered cap**: entity/device/integration rules never capped; domain pool capped unless `*` or Max Entities = 0
- Comma-separated rules (InfoPanel UI friendly) with optional quoting
- **Integration.Device containers** - e.g. `Bambu Lab.A1MINI`, `Backup.Backup`
- **Refresh interval** configurable in Plugins UI (default 5s, min 1s, max 300s)
- **WebSocket** with `subscribe_entities` (compressed deltas); automatic per-entity REST fallback when WS is down
- Connection status in **System** container

## Home Assistant concepts

| Term | In Home Assistant | In this plugin |
|------|-------------------|----------------|
| **Domain** | Entity type prefix (`sensor`, `light`, `input_boolean`) | `domain.sensor` rule or Entity Domains list |
| **Entity** | Full id (`sensor.temperature`, `light.room`) | `entity.sensor.temperature` or shorthand `light.room` |
| **Integration** | Platform that created the entity (`mqtt`, `backup`, `bambu_lab`) | `integration.backup` rule; also the first part of container names |
| **Device** | Physical or logical device grouping entities | `device."Front Door"` rule; container name is `{Integration}.{DeviceName}` |

Container names come from the HA device registry when available. Example: the backup integration's device appears as **Backup.Backup** - exclude it with `integration.backup`.

## Quick Start

### 1. Create a Home Assistant token

1. Open Home Assistant → your profile → **Security**
2. Create a **Long-Lived Access Token**

### 2. Install

```bash
dotnet build InfoPanel.HomeAssistant/InfoPanel.HomeAssistant/InfoPanel.HomeAssistant.csproj -c Release
```

Copy the Release output folder contents to:

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
| Max Entities | `2000` (pool safeguard; use `0` or `*` for unlimited) |
| Refresh Interval | `5` seconds (1-300) |

Click **Reload** after changing entity filters (Include, Domains, Exclude, Max Entities, URL, or token). The sensor tree refreshes without restarting InfoPanel (InfoPanel 1.4+).

Legacy saved keys (`UpdateMode`, `CatalogRefreshSeconds`) are ignored with a one-time log.

## Selection model

```text
Final set = (Include explicit + Include domains + EntityDomains) - Exclude
```

**Exclusions are absolute.** If you include all lights (`domain.light` or `*`) and exclude `light.room`, that entity is never listed, even when matched by an explicit device or integration rule.

| Tier | Rules | Cap |
|------|-------|-----|
| **Explicit** | `entity.*`, `device.*`, `integration.*` | Never capped |
| **Domain pool** | `EntityDomains`, `domain.*`, `*` in Include | Capped at **Max Entities** (default 2000) unless `*` or Max Entities = **0** |

**Unlimited entities:** set `Entity Include` or `Entity Domains` to `*`, or set **Max Entities** to `0`.

Example: `EntityDomains=sensor` + `device."My Printer"` + `MaxEntities=2000` → all printer entities + up to 2000 sensors.

Example: `EntityInclude=*` + `EntityExclude=domain.input_boolean, integration.backup` → all supported entities except Input Booleans and the Backup integration.

Connection status example: `OK (120 entities: 10 explicit + 110 from domains, 8 devices, subscribe_entities, 5s refresh, WS connected)`

## Transport and refresh

| Path | When | HA traffic |
|------|------|------------|
| **subscribe_entities** | WS connected (default) | Compressed snapshot + deltas for subscribed ids only |
| **state_changed** | subscribe_entities unavailable | Global events; non-selected entities ignored |
| **Per-entity REST** | WS disconnected | `GET /api/states/{entity_id}` for **dirty ids only** |
| **Bulk REST** | Reload / discovery | One `GET /api/states` for entity selection |

Home Assistant pushes state changes to the plugin cache immediately. The **refresh interval** controls how often dirty values are applied to InfoPanel entries. When nothing changed, poll cycles are skipped.

A safety full sync marks all entities dirty every 60 poll cycles.

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

Requires .NET 8 SDK and a local clone of the InfoPanel repository (for `InfoPanel.Plugins` references).

```
InfoPanel.HomeAssistant.Core/
  Configuration/     Settings and supported domains
  Mapping/           Grouping, slugs, state parsing
  Models/            API and discovery DTOs
  Rules/             Rule parsing
  Services/          API, registry, WebSocket state stream, selection
InfoPanel.HomeAssistant/
  Plugin/            IPlugin entry point and engine
  Plugin/Layout/     Discovery layout types
```

Build Release and copy output to `%ProgramData%\InfoPanel\plugins\InfoPanel.HomeAssistant\`, or distribute a release zip.

Set environment variable `INFOPANEL_PLUGIN_TEST=1` to enable verbose plugin logging to the InfoPanel logs folder.

## Troubleshooting

| Symptom | Check |
|---------|-------|
| Device entities missing | Use `device.*` or `integration.*` (explicit tier); domain cap does not truncate them |
| Too few entities with `*` or Max Entities = 0 | Click **Reload** after saving config; confirm plugin version is 0.6.2+ |
| Too many domain entities | Lower Max Entities, add Entity Exclude, or use targeted domains instead of `*` |
| Excluded entity still visible | Click **Reload**; exclusion applies at discovery time |
| `device.*` matches nothing | WebSocket registry required; try device UUID |
| Values stale while WS connected | Lower refresh interval; check connection status for `WS connected` |
| Reload does not update tree | Update InfoPanel to 1.4+ with plugin Reload support |

Logs: `%LOCALAPPDATA%\InfoPanel\logs\plugin-host-InfoPanel.HomeAssistant*.log`

## License

InfoPanel.HomeAssistant is licensed under **GPL-3.0-or-later**. See [LICENSE](LICENSE).

Copyright (C) 2026 CyanureChan
