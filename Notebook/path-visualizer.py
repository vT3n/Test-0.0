import json, os
from collections import defaultdict
import matplotlib.pyplot as plt
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
    return os.path.join(folder, files[0]) if files else None

def recent_file(folder):
    files = [f for f in os.listdir(folder) if f.endswith('.jsonl')]
    files.sort(key=lambda f: int(f.split('_')[1].split('.')[0]))  # convert to int
    return os.path.join(folder, files[-1]) if files else None

def points_by_level(records):
    by_level = defaultdict(list)
    for r in records:
        if "px" in r and "py" in r:
            lvl = r.get("level_name", "UNKNOWN")
            by_level[lvl].append((float(r["px"]), float(r["py"])))
    return by_level

def plot_list(points, name, same_tol=2.0):
    n = len(points)
    if n < 2:
        return

    # gradient colors for points
    t = np.linspace(0, 1, max(n - 1, 1))
    start = np.array([0.3, 0.3, 1.0])
    end   = np.array([1.0, 0.3, 0.3])
    colors = [tuple(start*(1 - val) + end*val) for val in t]

    # counts many labels already placed near the coordinate
    def key_xy(x, y):
        return (int(round(x / same_tol)), int(round(y / same_tol)))

    placed_counts = defaultdict(int)

    def offset_for(x, y):
        k = key_xy(x, y)
        idx = placed_counts[k]
        placed_counts[k] += 1
        r = 3.5  # pixels
        angle = (idx - 1) * (np.pi / 3 )  # 45° steps
        return r * np.cos(angle), r * np.sin(angle)

    tp = 1
    markersize = 10
    for i in range(n - 1):
        x1, y1 = points[i]
        x2, y2 = points[i + 1]
        length = np.hypot(x2 - x1, y2 - y1)

        if length > 20:  # Checks if Teleported
            c = colors[i]
            plt.plot(x1, y1, 'o', color=c, markersize=markersize)
            dx, dy = offset_for(x1, y1)
            if dx or dy:
                plt.plot([x1, x1 + dx], [y1, y1 + dy], linewidth=1, alpha=0.3, color=c)
            plt.text(x1 + dx, y1 + dy, str(tp), fontsize=9, ha='center', va='center',
                     color='white', bbox=dict(boxstyle='round,pad=0.18', fc='black', ec='none', alpha=0.55))

            plt.plot(x2, y2, 'o', color=c, markersize=markersize)
            dx2, dy2 = offset_for(x2, y2)
            if dx2 or dy2:
                plt.plot([x2, x2 + dx2], [y2, y2 + dy2], linewidth=1, alpha=0.3, color=c)
            plt.text(x2 + dx2, y2 + dy2, str(tp), fontsize=9, ha='center', va='center',
                     color='white', bbox=dict(boxstyle='round,pad=0.18', fc='black', ec='none', alpha=0.55))

            tp += 1
        else: # Checks if not Teleported, connects the points
            plt.plot([x1, x2], [y1, y2], color=colors[i], linewidth=2)

    plt.title(name)
    plt.xlabel("px")
    plt.ylabel("py")

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

