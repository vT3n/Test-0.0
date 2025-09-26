import json, numpy as np
import matplotlib.pyplot as plt

def load_events(path):
    with open(path, 'r', encoding='utf-8') as f:
        for line in f:
            s = line.strip()
            if s:
                yield json.loads(s)

def extents(rooms):
    mx, my = 0, 0
    for r in rooms:
        for a in r.get('walk_rle', []):
            mx = max(mx, a['x1'])
            my = max(my, a['y'])
        for a in r.get('pit_rle', []):
            mx = max(mx, a['x1'])
            my = max(my, a['y'])
    return mx + 1, my + 1

def render_level(ev, idx=0, pit_overlay=True):
    w, h = extents(ev['rooms'])
    walk = np.zeros((h, w), dtype=np.uint8)
    pits = np.zeros((h, w), dtype=np.uint8)
    for r in ev['rooms']:
        for a in r.get('walk_rle', []):
            walk[a['y'], a['x0']:a['x1'] + 1] = 1
        for a in r.get('pit_rle', []):
            pits[a['y'], a['x0']:a['x1'] + 1] = 1
    fig = plt.figure()
    plt.imshow(walk, origin='lower', interpolation='nearest')
    if pit_overlay and pits.any():
        m = np.ma.masked_where(pits == 0, pits)
        plt.imshow(m, origin='lower', interpolation='nearest', alpha=0.35)
    plt.title(ev.get('level_name', 'UNKNOWN'))
    try:
        win = fig.canvas.manager.window
        win.move(50 * idx, 50 * idx)
    except Exception:
        pass

def render_file(path):
    i = 0
    for ev in load_events(path):
        if ev.get('event') == 'floor_walkmap':
            render_level(ev, i)
            i += 1
    plt.show()

# usage:
render_file("Notebook/test.jsonl")
