import sys, re, struct, json, os, UnityPy
from UnityPy.enums import ClassIDType
pat = re.compile(rb'(container_[A-Za-z0-9_]{6,}|Lootable_[0-9]{5})')

def qrot(q, v):
    x, y, z, w = q.x, q.y, q.z, q.w
    vx, vy, vz = v
    # t = 2 * cross(q.xyz, v)
    tx = 2 * (y * vz - z * vy); ty = 2 * (z * vx - x * vz); tz = 2 * (x * vy - y * vx)
    return (vx + w * tx + (y * tz - z * ty), vy + w * ty + (z * tx - x * tz), vz + w * tz + (x * ty - y * tx))

def world_pos(sf, tr_pid, cache):
    chain = []
    pid = tr_pid
    while pid:
        o = sf.objects.get(pid)
        if o is None: return None
        t = cache.get(pid)
        if t is None:
            t = o.read(); cache[pid] = t
        chain.append(t)
        f = t.m_Father
        if f.m_FileID != 0: return None
        pid = f.m_PathID
    p = (0.0, 0.0, 0.0)
    for t in chain:  # child first: apply child's local, then parents outward
        s = t.m_LocalScale; lp = t.m_LocalPosition
        p = (p[0] * s.x, p[1] * s.y, p[2] * s.z)
        p = qrot(t.m_LocalRotation, p)
        p = (p[0] + lp.x, p[1] + lp.y, p[2] + lp.z)
    return p

results = []
for path in sys.argv[1:]:
    name = os.path.basename(path)
    try:
        env = UnityPy.load(path); sf = list(env.files.values())[0]
    except Exception as e:
        print(json.dumps({"file": name, "error": str(e)}), file=sys.stderr); continue
    cache = {}
    for pid, o in sf.objects.items():
        if o.type != ClassIDType.MonoBehaviour: continue
        raw = o.get_raw_data()
        m = pat.search(raw)
        if not m or m.start() > 200: continue
        ln = struct.unpack_from('<i', raw, m.start() - 4)[0]
        cid = raw[m.start():m.start() + ln].decode('utf8', 'replace')
        fid, gpid = struct.unpack_from('<iq', raw, 0)
        if fid != 0: continue
        try:
            go = sf.objects[gpid].read()
            tr = None
            for c in go.m_Component:
                ptr = c.component if hasattr(c, 'component') else c[1] if isinstance(c, tuple) else c
                if ptr.m_FileID == 0 and sf.objects.get(ptr.m_PathID) and sf.objects[ptr.m_PathID].type == ClassIDType.Transform:
                    tr = ptr.m_PathID; break
            pos = world_pos(sf, tr, cache) if tr else None
        except Exception as e:
            pos = None
        if pos:
            results.append({"id": cid, "x": round(pos[0], 2), "y": round(pos[1], 2), "z": round(pos[2], 2), "go": go.m_Name, "file": name})
for r in results: print(json.dumps(r))
