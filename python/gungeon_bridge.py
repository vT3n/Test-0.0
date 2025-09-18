"""Utilities for receiving game state snapshots from the GungeonRLTracker BepInEx plugin."""
from __future__ import annotations

import json
import logging
import socket
import threading
import time
from dataclasses import dataclass
from queue import Empty, Queue
from typing import Any, Dict, Optional

LOGGER = logging.getLogger(__name__)


@dataclass
class Snapshot:
    """Wrapper for the JSON snapshot emitted by the plugin."""

    sequence: int
    realtime: float
    payload: Dict[str, Any]

    @property
    def player(self) -> Dict[str, Any]:
        return self.payload.get("player", {})

    @property
    def enemies(self) -> Any:
        return self.payload.get("enemies", [])

    @property
    def projectiles(self) -> Any:
        return self.payload.get("projectiles", [])

    @property
    def room(self) -> Any:
        return self.payload.get("room", {})


class GungeonBridge:
    """Connects to the mod and exposes the latest snapshot to Python code."""

    def __init__(
        self,
        host: str = "127.0.0.1",
        port: int = 18475,
        reconnect_delay: float = 1.0,
        buffer_size: int = 128,
    ) -> None:
        self.host = host
        self.port = port
        self.reconnect_delay = reconnect_delay
        self._buffer: "Queue[Snapshot]" = Queue(maxsize=buffer_size)
        self._thread: Optional[threading.Thread] = None
        self._stop_event = threading.Event()
        self._socket_lock = threading.Lock()
        self._socket: Optional[socket.socket] = None

    def start(self) -> None:
        """Start the background reader thread."""

        if self._thread and self._thread.is_alive():
            return
        self._stop_event.clear()
        self._thread = threading.Thread(target=self._run, name="GungeonBridgeReader", daemon=True)
        self._thread.start()
        LOGGER.info("Started bridge reader thread")

    def close(self) -> None:
        """Stop the reader thread and close the socket."""

        self._stop_event.set()
        if self._thread:
            self._thread.join(timeout=1.0)
        with self._socket_lock:
            if self._socket:
                try:
                    self._socket.close()
                finally:
                    self._socket = None

    def get_latest_snapshot(self, timeout: Optional[float] = None) -> Optional[Snapshot]:
        """Return the next snapshot from the queue, or ``None`` if none arrive in time."""

        try:
            return self._buffer.get(timeout=timeout)
        except Empty:
            return None

    def _run(self) -> None:
        while not self._stop_event.is_set():
            try:
                self._ensure_socket()
                self._receive_loop()
            except Exception as exc:  # pragma: no cover - defensive logging
                LOGGER.warning("Bridge reader error: %s", exc, exc_info=True)
                self._close_socket()
                time.sleep(self.reconnect_delay)

    def _ensure_socket(self) -> None:
        with self._socket_lock:
            if self._socket:
                return
            LOGGER.info("Connecting to %s:%s", self.host, self.port)
            sock = socket.create_connection((self.host, self.port), timeout=2.0)
            sock.settimeout(2.0)
            self._socket = sock

    def _close_socket(self) -> None:
        with self._socket_lock:
            if not self._socket:
                return
            try:
                self._socket.close()
            finally:
                self._socket = None

    def _receive_loop(self) -> None:
        assert self._socket is not None
        buffer = b""
        while not self._stop_event.is_set():
            chunk = self._socket.recv(4096)
            if not chunk:
                raise ConnectionError("Socket closed by remote host")
            buffer += chunk
            while b"\n" in buffer:
                line, buffer = buffer.split(b"\n", 1)
                line = line.strip()
                if not line:
                    continue
                self._handle_message(line)

    def _handle_message(self, raw: bytes) -> None:
        try:
            message = json.loads(raw.decode("utf-8"))
        except json.JSONDecodeError as exc:
            LOGGER.debug("Dropping malformed message: %s", exc)
            return

        message_type = message.get("message_type")
        if message_type == "handshake":
            LOGGER.info(
                "Connected to %s (schema v%s, plugin v%s)",
                message.get("game"),
                message.get("schema_version"),
                message.get("plugin_version"),
            )
            return

        if message_type != "snapshot":
            LOGGER.debug("Ignoring message type %s", message_type)
            return

        snapshot = Snapshot(
            sequence=int(message.get("sequence", 0)),
            realtime=float(message.get("realtime", 0.0)),
            payload=message,
        )

        if not self._buffer.full():
            self._buffer.put_nowait(snapshot)
        else:  # pragma: no cover - drop oldest if saturated
            try:
                _ = self._buffer.get_nowait()
            except Empty:
                pass
            self._buffer.put_nowait(snapshot)


__all__ = ["GungeonBridge", "Snapshot"]
