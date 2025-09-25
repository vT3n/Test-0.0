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

def plot_list(points, name):
    n = len(points)
    t = np.linspace(0, 1, n - 1)
    start = np.array([0.3, 0.3, 1.0]) 
    end   = np.array([1.0, 0.3, 0.3]) 
    colors = [tuple(start*(1 - val) + end*val) for val in t]

    for i in range(len(points) - 1):
        x1, y1 = points[i]
        x2, y2 = points[i + 1]
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

