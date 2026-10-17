# Container positions

SPT's `staticContainers.json` stores (0, 0, 0) for every container. The client reads
the position from the map scene, so the real positions only exist in the game files.
These scripts extract them once and write `wwwroot/maps/container-positions.json`.

Requirements: Python 3.10+ and `pip install UnityPy "attrs>=23"`.

1. Find the scene files that hold container Ids (run inside `EscapeFromTarkov_Data`):

   ```
   grep -c -a -E "container_[A-Za-z0-9_]{6,}|Lootable_[0-9]{5}" level* | grep -v ":0$" | sort -t: -k2 -nr > levels_with_ids.txt
   ```

2. Read every container component in those scenes, with its world position:

   ```
   python3 extract.py $(cut -d: -f1 levels_with_ids.txt) > all.jsonl
   ```

3. Match the Ids with each map's `staticContainers.json` (set `SPT_PATH` to your game folder). This writes `container_positions.json` and checks the result against
   the game's own `AIPatrolsData` list for Customs:

   ```
   SPT_PATH=/path/to/SPT python3 join.py
   ```

4. Convert it to the compact format the mod reads (`{ "maps": { "<map>": { "<id>": [x, y, z] } } }`)
   and replace `wwwroot/maps/container-positions.json`.

Each scene file is assigned to the map whose Ids it holds the most of, so generic Ids that
repeat in other scenes do not leak positions between maps.
