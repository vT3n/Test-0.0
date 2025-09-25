import json, os
from collections import defaultdict
import matplotlib.pyplot as plt
from matplotlib.collections import LineCollection
import numpy as np

def cleaning_data(data):
    cleaned_data = []
    for row in data:
        if str(row.get("health", "-1")) != "-1":
            cleaned_data.append(row)
    return cleaned_data

def load_run_data(path):
    with open(path, 'r', encoding='utf-8') as f:
        for line in f:
            if line.strip():
                try:
                    yield json.loads(line)
                except json.JSONDecodeError as e:
                    print("JSON error:", e)
                    print("Line content:", line)

def random_file(folder):
    files = [f for f in os.listdir(folder) if f.endswith('.jsonl')]
    i = np.random.randint(0, len(files))
    return os.path.join(folder, files[i]) if files else None

def recent_file(folder):
    files = [f for f in os.listdir(folder) if f.endswith('.jsonl')]
    files.sort(key=lambda f: int(f.split('_')[1].split('.')[0])) 
    return os.path.join(folder, files[-1]) if files else None

def points_by_level(records):
    by_level = defaultdict(list)
    for r in records:
        if "px" in r and "py" in r:
            lvl = r.get("level_name", "UNKNOWN")
            by_level[lvl].append((float(r["px"]), float(r["py"])))
    return by_level

def plot_list(points, name, same_tol=10):
    n = len(points)
    if n == 0:
        plt.title(name)
        return
    if n == 1:
        x, y = points[0]
        plt.scatter([x], [y], s=60)
        plt.text(x, y, "Start", fontsize=9, ha='center', va='center',
                 color='white', bbox=dict(boxstyle='round,pad=0.18', fc='black', ec='none', alpha=0.8))
        plt.text(x, y, "End", fontsize=9, ha='center', va='center',
                 color='white', bbox=dict(boxstyle='round,pad=0.18', fc='black', ec='none', alpha=0.8))
        plt.title(name)
        return

    t = np.linspace(0, 1, max(n - 1, 1))
    start = np.array([0.3, 0.3, 1.0])
    end   = np.array([1.0, 0.3, 0.3])
    colors = [tuple(start*(1 - val) + end*val) for val in t]

    def bucket(x, y):
        return (int(round(x / same_tol)), int(round(y / same_tol)))

    placed_counts = defaultdict(int)

    def offset_for(x, y):
        key = bucket(x, y)
        idx = placed_counts[key]
        placed_counts[key] += 1
        if idx == 0:
            return 0.0, 0.0
        r = 4
        angle = (idx - 1) * np.pi
        return r * np.cos(angle), r * np.sin(angle)

    pts = np.asarray(points, float)
    segs = np.stack([pts[:-1], pts[1:]], axis=1)
    lens = np.hypot(pts[1:,0]-pts[:-1,0], pts[1:,1]-pts[:-1,1])
    short_mask = lens <= 20

    ax = plt.gca()
    if np.any(short_mask):
        lc = LineCollection(segs[short_mask], colors=[colors[i] for i in np.nonzero(short_mask)[0]], linewidths=2)
        ax.add_collection(lc)
        ax.autoscale()

    jump_idx = np.nonzero(~short_mask)[0]
    tp = 1
    for i in jump_idx:
        for (x, y) in [pts[i], pts[i+1]]:
            dx, dy = offset_for(x, y)
            if dx or dy:
                ax.add_collection(LineCollection([[[x, y], [x+dx, y+dy]]], linewidths=1, alpha=0.3, colors=[colors[i]]))
            plt.text(x+dx, y+dy, str(tp), fontsize=9, ha='center', va='center',
                     color='white', bbox=dict(boxstyle='round,pad=0.18', fc='black', ec='none', alpha=0.7))
            tp += 1

    pad = 1.0

    x1, y1 = points[0]
    x2, y2 = points[-1]
    plt.scatter(pts[:,0], pts[:,1], s=1, alpha=0.35)
    ax.set_xlim(pts[:,0].min()-pad, pts[:,0].max()+pad)
    ax.set_ylim(pts[:,1].min()-pad, pts[:,1].max()+pad)
    plt.text(x1, y1, "Start", fontsize=9, ha='center', va='center',
             color='white', bbox=dict(boxstyle='round,pad=0.18', fc='black', ec='none', alpha=0.8))
    plt.text(x2, y2, "End", fontsize=9, ha='center', va='center',
             color='white', bbox=dict(boxstyle='round,pad=0.18', fc='black', ec='none', alpha=0.8))
    plt.title(name)


file_path = "Notebook/Runs"
# file_path = random_file(file_path)
file_path = recent_file(file_path)

data = list(load_run_data(file_path))
data = cleaning_data(data)

# data_dict = data[1]
# print(data_dict.keys(), "\n")
# print(data_dict.items(), "\n")
# print(data)

levels = points_by_level(data)

for name, points in levels.items():
    if name == "Loading":
        continue
    print(len(points), name)
    fig = plt.figure()        
    plot_list(points, name)
    save_dir = "Notebook/Plots"
    if not os.path.exists(save_dir):
        os.makedirs(save_dir)
    save_path = os.path.join(save_dir, f"{name}.png")
    fig.savefig(save_path)

plt.show()  

