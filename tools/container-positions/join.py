import json, os, re, struct, math, collections, sys
import UnityPy
SPT = os.environ.get('SPT_PATH', '.')
base = os.path.join(SPT, 'SPT_Runtime', 'SPT_Data', 'database', 'locations')
recs = [json.loads(l) for l in open('all.jsonl')]
by_id = collections.defaultdict(list)
for r in recs: by_id[r['id']].append(r)
# Each scene file belongs to the map(s) whose IDs it holds the most of
maps_ids = {}
for m in sorted(os.listdir(base)):
    p = os.path.join(base, m, 'staticContainers.json')
    if os.path.exists(p): maps_ids[m] = {c['template']['Id'] for c in json.load(open(p)).get('staticContainers', [])}
file_ids = collections.defaultdict(set)
for r in recs: file_ids[r['file']].add(r['id'])
owner = {}
for f, ids_ in file_ids.items():
    scores = {m: len(ids_ & s) for m, s in maps_ids.items()}
    best = max(scores.values())
    if best > 0: owner[f] = {m for m, v in scores.items() if v == best}
out = {}
print(f"{'map':16} {'spt':>5} {'found':>5} {'dupPos':>6} files")
for m in sorted(os.listdir(base)):
    p = os.path.join(base, m, 'staticContainers.json')
    if not os.path.exists(p): continue
    sc = json.load(open(p)).get('staticContainers', [])
    ids = [c['template']['Id'] for c in sc]
    # choose, per map, the scene files that cover most of its ids
    filecount = collections.Counter(r['file'] for i in ids for r in by_id.get(i, []))
    good = {f for f in filecount if m in owner.get(f, set())}
    found, amb, pts = 0, 0, []
    for c in sc:
        cands = sorted((r for r in by_id.get(c['template']['Id'], []) if r['file'] in good and r['go'] != 'AIPatrolsData' and not r['go'].startswith('Sit ')), key=lambda r: -filecount[r['file']])
        if not cands: continue
        found += 1
        uniq = {(r['x'], r['y'], r['z']) for r in cands}
        if len({r['file'] for r in cands}) > 1 and len(uniq) > 1: amb += 1
        r = cands[0]
        pts.append({"id": c['template']['Id'], "tpl": c['template']['Items'][0]['_tpl'], "x": r['x'], "y": r['y'], "z": r['z']})
    out[m] = pts
    print(f"{m:16} {len(ids):5} {found:5} {amb:6} {sorted(filecount.items(), key=lambda kv:-kv[1])[:3]}")
json.dump(out, open('container_positions.json', 'w'))

# Independent check against the game's AIPatrolsData list for Customs (optional)
try:
    from UnityPy.enums import ClassIDType
    env = UnityPy.load(os.path.join(SPT, 'EscapeFromTarkov_Data', 'level4')); sf = list(env.files.values())[0]
    raw = None
    for o in sf.objects.values():
        if o.type != ClassIDType.MonoBehaviour: continue
        data = o.get_raw_data()
        if data.count(b'container_') > 100: raw = data; break
    pos = {}
    for mm in re.finditer(rb'container_[A-Za-z0-9_]+', raw or b''):
        ln = struct.unpack_from('<i', raw, mm.start() - 4)[0]
        end = mm.start() + ln; end += (-end) % 4
        pos[raw[mm.start():mm.start()+ln].decode()] = struct.unpack_from('<fff', raw, end + 16)
    cust = {p['id']: p for p in out.get('bigmap', [])}
    d = sorted(math.dist((cust[k]['x'], cust[k]['y'], cust[k]['z']), v) for k, v in pos.items() if k in cust)
    if d:
        print("AIPatrolsData check (customs):", len(d), "matched; median dist", round(d[len(d)//2], 2), "max", round(d[-1], 2))
except Exception as e:
    print("AIPatrolsData check skipped:", e)
