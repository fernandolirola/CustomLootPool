# Changes

## 1.5.0

### New

- Loot quality: a new card makes valuable or rare items more common in containers and loose loot without changing how many items spawn. Boost by price tier (flea, or handbook when the flea has none) or by the game's rarity, on all maps or only the ones you pick. Keys and keycards are left alone by default.
- The boost runs before your list's chances, so configured items still get exactly their share.
- A preview per map shows each tier's share and the average item value before and after. "Simulate raids" uses the card's settings, even before saving.
- Off by default, so updating changes nothing until you turn it on.

## 1.4.0

### New

- Change the item of any card without losing its settings: click the item name (or the swap button) and pick the new item. Undo is offered right after.
- Duplicate now opens the copy on the item picker, so you can reuse a finished setup for another item in two clicks. "Keep" leaves the copy on the same item, for another % on other maps or dates.
- When the old item's own category was the only loot pool picked, it follows the new item; hand-picked pools stay as they were.
- Loot Atlas: markers keep a readable size while zooming instead of growing with the map.
- Loot Atlas: hovering (or focusing) a container shows a card with its name and picture (pictures of all 45 container types ship with the mod, from the Escape from Tarkov Wiki), how many are stacked there and, after a simulation, the items per raid. Loose loot spots show their details the same way.

### Fixes

- Simulated raids labeled every container as "Container"; they now show the real container type.
- Items with presets (weapons, armor) placed in airdrops were not marked found in raid.
- Regression tests did not build after container positions were added.

### Cleanup

- Remove unused styles and redundant project settings.

## 1.3.1

- Loot Atlas now shows containers. Stock SPT stores (0, 0, 0) for every static container, so their real positions were extracted from the game scenes and ship in `wwwroot/maps/container-positions.json` (99.7% of containers on every map, matched by instance Id).
- Add `tools/container-positions` to regenerate that file after a game update.
- Containers are drawn as markers instead of heat: containers stacked in the same place become one marker with a count, and the tooltip lists their types. The density glow now comes from loose loot only and can be turned off.
- Densest cells show how many containers and loose spots each area has.

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
