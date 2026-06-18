# InfoPanel.HomeAssistant

Home Assistant integration for [InfoPanel](https://github.com/hongyinghsl/InfoPanel) **1.4+**. **v0.3.1** lets you pick specific entities and groups them by device in the Plugins tree.

## Features

- Connect via Long-Lived Access Token (REST + WebSocket registry)
- **Entity Include** rules - pick exact entities, devices, integrations, or domains
- **Integration.Device containers** - e.g. `MQTT.Front Door` with entities inside
- Fallback domain filter when Entity Include is empty
- Cap entity count (default **20**, up to 200)
- `binary_sensor.*` → text; other numeric domains → sensors
- Poll every 15 seconds; connection status in **System** container

## Quick Start

### 1. Create a Home Assistant token

1. Open Home Assistant → your profile → **Security**
2. Create a **Long-Lived Access Token**

### 2. Install

```bash
dotnet build InfoPanel.HomeAssistant/InfoPanel.HomeAssistant/InfoPanel.HomeAssistant.csproj -c Release
```

Credentials (Base URL and Access Token) are configured in the InfoPanel Plugins UI only - nothing is stored in this repository.

### Install to InfoPanel

After building Release output, copy the folder to:

```
%ProgramData%\InfoPanel\plugins\InfoPanel.HomeAssistant\
```

Restart InfoPanel, then configure the plugin under **Plugins → Home Assistant**.

### 3. Configure (Plugins UI)

| Setting | Example |
|---------|---------|
| Base URL | `https://homeassistant.example.com` |
| Access Token | Your long-lived token |
| Entity Include | See rules below |
| Entity Domains | `sensor,climate,binary_sensor` (fallback when Include is empty) |
| Max Entities | `20` |

Click **Reload** after changing Entity Include or filters.

## Entity Include rules

One rule per line in the **Entity Include** field:

| Rule | Meaning |
|------|---------|
| `*` | All **supported** entity types (see below), still capped by Max Entities |
| `entity.binary_sensor.frontdoor_contact` | Exact entity |
| `binary_sensor.frontdoor_contact` | Shorthand exact entity |
| `domain.sensor` | All entities in a domain |
| `integration.bambu_lab` | All entities from the Bambu Lab integration |
| `integration.mqtt` | All entities from an integration/platform |
| `device.My Printer` | All entities on a device (by HA device name) |
| `device.<device_id>` | All entities on a device (by HA UUID) |

Leave **Entity Include** empty to use **Entity Domains** + **Max Entities**.

### Supported entity types

Only domains that map to InfoPanel sensors/text are exposed. These are included when you use `*` or `domain.*`:

`sensor`, `binary_sensor`, `climate`, `number`, `input_number`, `switch`, `input_boolean`, `cover`, `lock`

**Not exposed:** `automation`, `script`, `button`, `input_text`, `scene`, `group`, `person`, `update`, etc.

### Entity Domains vs Entity Include

| Field | When it applies |
|-------|-----------------|
| **Entity Include** | When non-empty - **Entity Domains is ignored** |
| **Entity Domains** | Fallback when Entity Include is empty |
| `*` in either field | All **supported** types above (not every HA entity) |

**Max Entities** sorts matches A–Z by entity id and takes the first N. With `*` and Max Entities = 20, you only get the first 20 supported entities alphabetically - raise Max Entities or use specific include rules (recommended).

Example for a Bambu printer bed temperature sensor:

```text
entity.sensor.my_printer_bed_temperature
integration.bambu_lab
```

## Plugins tree layout

InfoPanel supports three levels: Plugin → Container → Entry.

```
Home Assistant
├── System
│   └── Connection Status
├── MQTT.Front Door
│   ├── binary_sensor.contact
│   └── sensor.battery
└── ZHA.Kitchen Thermostat
    └── sensor.temperature
```

## Binding paths

Plugin ID: `home-assistant-plugin`

| Entry | Example path |
|-------|--------------|
| Entity | `/home-assistant-plugin/mqtt-front-door/sensor-battery` |
| Status | `/home-assistant-plugin/system/connection-status` |

Container IDs are URL-safe slugs derived from `Integration.Device` names.

## Development

Requires .NET 8 SDK and a local clone of the InfoPanel repository.

### Solution layout

```
InfoPanel.HomeAssistant.Core/   API, registry, selection, grouping
InfoPanel.HomeAssistant/         Plugin (references Core + InfoPanel.Plugins)
```

### Deploy locally

Build Release, then copy `InfoPanel.HomeAssistant/bin/Release/net8.0/InfoPanel.HomeAssistant-v{VERSION}/InfoPanel.HomeAssistant/` to `%ProgramData%\InfoPanel\plugins\InfoPanel.HomeAssistant\`.

Or import a flat ZIP named `InfoPanel.HomeAssistant.zip` via **Plugins → Import Plugin Archive**.

### Plugin Simulator

```bash
dotnet run --project infopanel/InfoPanel.Plugins.Simulator/InfoPanel.Plugins.Simulator.csproj
```

## Troubleshooting

| Symptom | Check |
|---------|-------|
| Plugin not in list | Folder/DLL naming; restart after manual copy |
| No entities after config | Click **Reload** in Plugins UI |
| `integration.*` / `device.*` rules match nothing | WebSocket registry must be reachable; check firewall |
| Registry unavailable message | Grouping falls back to domain-based heuristics; WS may be blocked |
| `socket ... forbidden` | Plugin falls back to `curl.exe` for REST |
| Import ZIP fails | Name must be `InfoPanel.HomeAssistant.zip`; flat layout |

Logs: `%LOCALAPPDATA%\InfoPanel\logs\plugin-host-InfoPanel.HomeAssistant*.log`

## License

InfoPanel.HomeAssistant is licensed under **GPL-3.0-or-later**, the same license as [InfoPanel](https://github.com/habibrehmansg/infopanel). See [LICENSE](LICENSE) for the full text.

Copyright (C) 2026 CyanureChan
