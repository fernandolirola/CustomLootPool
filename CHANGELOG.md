# Changes

## 1.3.0

### Improvements

- Add Loot Atlas with floor selection, container and loose-loot layers, landmark labels, zoom and pan, and ranked density cells.
- Compare eligible locations with item frequency from 25 or 100 simulated raids using the current editor settings.
- Include attributed Tarkov.dev SVG maps for nine maps, covering eleven SPT locations. Map artwork remains under its original license.
- Clarify that preview spots refer to loose loot.
- Use the SPT panel theme and native MudBlazor controls in Loot Atlas.
- Remove unused helpers and redundant map-provider state, share preview selection rules, and simplify validation and styling.

- Choose which airdrop crate types can contain each configured item. Existing configurations continue to allow every type.

## 1.2.1

- Build with SPTushonka 4.1.6 packages, target SPT ~4.1.6 and align runtime metadata with Forge naming requirements.

- Solve probabilities jointly for containers, loose-loot points and bots. At a
  total of 100% or more, custom shares are normalized and vanilla shares become
  zero. Duplicate active entries use their maximum requested chance.
- Adjust loose-loot items already present, preserving existing roots, keys and
  preset alternatives. Add a new root only when necessary.
- Validate Load/Import/Save inputs, including null objects/lists, item IDs,
  enum values, dates, finite numbers and map overrides. Preserve case-insensitive
  map lookup across JSON roundtrips.
- Save with an expected revision and serialize writers. Flush a temporary file
  and rename it over the config. Failed validation/conflicts keep disk and live
  state unchanged. Invalid loads retain the last valid config and save a backup.
- Show editor conflicts and reload clean tabs when configuration changes.
- Simulate with a dedicated generator/counter pair. Apply and clear map spawn
  caps for every raid, including failures; share a raid's cap across static and
  loose generation. Check cancellation per raid and simulate the full editor
  configuration. Clone loose-loot inputs before native generation mutates them.
- Allow 4/25/100 simulated raids and explain small-sample uncertainty.
- Filter similar-item statistics by the selected maps and deduplicate overlapping
  category contributions by pool.
- Scope bot/airdrop map selection to each session request using AsyncLocal and
  exception-safe Harmony finalizers. Unknown contexts skip map-restricted entries.
- Handle failed pool-index initialization in the page, offer Retry, and allow a
  failed initialization task to be rebuilt.
- Add 26 regression cases, including in-memory probability/inventory models,
  concurrent saves and native SPT 4.1.6 patch attachment. No live profiles or game
  sessions are used by these tests.
- Include a default config.json for new installations. Keep existing configuration files when upgrading.
