"""Minimal DQN-style training loop skeleton that consumes snapshots from the tracker."""
from __future__ import annotations

import math
import random
from collections import deque
from typing import Deque, Iterable, List, Optional, Tuple

import numpy as np
import torch
import torch.nn as nn
import torch.optim as optim

from gungeon_bridge import GungeonBridge, Snapshot


class DQN(nn.Module):
    """Tiny fully-connected network for demonstration purposes."""

    def __init__(self, input_dim: int, output_dim: int, hidden_dim: int = 128) -> None:
        super().__init__()
        self.net = nn.Sequential(
            nn.Linear(input_dim, hidden_dim),
            nn.ReLU(),
            nn.Linear(hidden_dim, hidden_dim),
            nn.ReLU(),
            nn.Linear(hidden_dim, output_dim),
        )

    def forward(self, x: torch.Tensor) -> torch.Tensor:  # type: ignore[override]
        return self.net(x)


class ReplayBuffer:
    def __init__(self, capacity: int = 100_000) -> None:
        self.capacity = capacity
        self._buffer: Deque[Tuple[np.ndarray, int, float, np.ndarray, bool]] = deque(maxlen=capacity)

    def push(
        self,
        state: np.ndarray,
        action: int,
        reward: float,
        next_state: np.ndarray,
        done: bool,
    ) -> None:
        self._buffer.append((state, action, reward, next_state, done))

    def sample(self, batch_size: int) -> Tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
        batch = random.sample(self._buffer, batch_size)
        states, actions, rewards, next_states, dones = map(np.array, zip(*batch))
        return states, actions, rewards, next_states, dones

    def __len__(self) -> int:
        return len(self._buffer)


ACTION_SPACE = [
    "idle",
    "move_left",
    "move_right",
    "move_up",
    "move_down",
    "shoot",
    "dodge_roll",
]


def extract_features(snapshot: Snapshot, max_enemies: int = 6, max_projectiles: int = 10) -> np.ndarray:
    """Flatten relevant parts of the snapshot into a fixed-size feature vector."""

    player = snapshot.player
    features: List[float] = []

    # Player stats
    features.extend(
        [
            player.get("health", 0.0),
            player.get("max_health", 0.0),
            player.get("armor", 0.0),
            player.get("blanks", 0),
            player.get("money", 0),
            player.get("keys", 0),
            float(player.get("is_dodge_rolling", False)),
        ]
    )

    position = player.get("position", [0.0, 0.0])
    velocity = player.get("velocity", [0.0, 0.0])
    features.extend(position)
    features.extend(velocity)

    # Enemies (pad/truncate to fixed number)
    enemies = list(snapshot.enemies)[:max_enemies]
    for enemy in enemies:
        features.extend(
            [
                enemy.get("health", 0.0),
                enemy.get("max_health", 0.0),
                float(enemy.get("is_boss", False)),
                enemy.get("distance_to_player", 0.0),
            ]
        )
        enemy_pos = enemy.get("position", [0.0, 0.0])
        features.extend(enemy_pos)
    while len(enemies) < max_enemies:
        features.extend([0.0, 0.0, 0.0, 0.0, 0.0, 0.0])
        enemies.append({})

    # Projectiles (pad/truncate)
    projectiles = list(snapshot.projectiles)[:max_projectiles]
    for projectile in projectiles:
        features.extend(
            [
                projectile.get("speed", 0.0),
                float(projectile.get("is_enemy", False)),
            ]
        )
        projectile_pos = projectile.get("position", [0.0, 0.0])
        projectile_dir = projectile.get("direction", [0.0, 0.0])
        features.extend(projectile_pos)
        features.extend(projectile_dir)
    while len(projectiles) < max_projectiles:
        features.extend([0.0, 0.0, 0.0, 0.0, 0.0, 0.0])
        projectiles.append({})

    return np.array(features, dtype=np.float32)


def select_action(model: DQN, state: np.ndarray, epsilon: float) -> int:
    if random.random() < epsilon:
        return random.randrange(len(ACTION_SPACE))
    state_tensor = torch.from_numpy(state).unsqueeze(0)
    with torch.no_grad():
        q_values = model(state_tensor)
    return int(q_values.argmax(dim=1).item())


def compute_td_loss(
    model: DQN,
    target_model: DQN,
    buffer: ReplayBuffer,
    batch_size: int,
    gamma: float,
    device: torch.device,
) -> torch.Tensor:
    states, actions, rewards, next_states, dones = buffer.sample(batch_size)

    states = torch.from_numpy(states).float().to(device)
    actions = torch.from_numpy(actions).long().to(device)
    rewards = torch.from_numpy(rewards).float().to(device)
    next_states = torch.from_numpy(next_states).float().to(device)
    dones = torch.from_numpy(dones.astype(np.float32)).float().to(device)

    q_values = model(states).gather(1, actions.unsqueeze(1)).squeeze(1)
    next_q_values = target_model(next_states).max(1)[0]
    expected_q = rewards + gamma * next_q_values * (1 - dones)

    return nn.functional.mse_loss(q_values, expected_q.detach())


def main() -> None:
    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")

    bridge = GungeonBridge()
    bridge.start()

    dummy_snapshot = Snapshot(sequence=0, realtime=0.0, payload={})
    feature_dim = extract_features(dummy_snapshot).shape[0]
    action_dim = len(ACTION_SPACE)

    policy_net = DQN(feature_dim, action_dim).to(device)
    target_net = DQN(feature_dim, action_dim).to(device)
    target_net.load_state_dict(policy_net.state_dict())

    optimizer = optim.Adam(policy_net.parameters(), lr=1e-3)
    buffer = ReplayBuffer()

    epsilon_start = 1.0
    epsilon_final = 0.05
    epsilon_decay = 30_000
    gamma = 0.99
    batch_size = 64
    target_update_interval = 1_000
    global_step = 0

    try:
        while True:
            snapshot = bridge.get_latest_snapshot(timeout=1.0)
            if snapshot is None:
                continue

            state = extract_features(snapshot)

            epsilon = epsilon_final + (epsilon_start - epsilon_final) * math.exp(-1.0 * global_step / epsilon_decay)
            action = select_action(policy_net, state, epsilon)

            reward = compute_reward(snapshot)
            next_snapshot = bridge.get_latest_snapshot(timeout=0.05)
            if next_snapshot is None:
                continue
            next_state = extract_features(next_snapshot)

            done = is_episode_done(next_snapshot)
            buffer.push(state, action, reward, next_state, done)

            if len(buffer) >= batch_size:
                loss = compute_td_loss(policy_net, target_net, buffer, batch_size, gamma, device)
                optimizer.zero_grad()
                loss.backward()
                optimizer.step()

            if global_step % target_update_interval == 0:
                target_net.load_state_dict(policy_net.state_dict())

            global_step += 1
    finally:
        bridge.close()


def compute_reward(snapshot: Snapshot) -> float:
    """Placeholder reward function based on player/enemy health."""

    player = snapshot.player
    enemies: Iterable = snapshot.enemies

    reward = 0.0
    reward += player.get("money", 0) * 0.01
    reward += player.get("health", 0.0) * 0.1
    reward -= sum(enemy.get("health", 0.0) for enemy in enemies) * 0.05
    return float(reward)


def is_episode_done(snapshot: Snapshot) -> bool:
    player = snapshot.player
    if player.get("health", 0.0) <= 0:
        return True
    room = snapshot.room or {}
    if room.get("enemies_remaining", 1) == 0:
        return True
    return False


if __name__ == "__main__":
    main()
