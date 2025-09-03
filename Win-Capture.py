"""
Windows screen/window capture – documented, high‑FPS example.

What this script does
- Starts a Windows capture session and receives frames via callbacks.
- Keeps the per‑frame callback lightweight to maximize FPS (no PNG saving).
- Prints FPS once per second.

About the decorators (@capture.event)
- The library exposes an event system. Decorating a function with
  @capture.event registers it as a handler for a specific event name,
  based on the function’s name:
    - on_frame_arrived: called when a new frame is available.
    - on_closed: called when the capture item/session closes.
- You don’t call these functions yourself; the capture thread invokes
  them when the event fires.

Capture lifecycle (high level)
1) Configure a WindowsCapture instance (what to capture/how).
2) Register event handlers with @capture.event.
3) Start the capture via capture.start().
4) The background thread pushes frames to on_frame_arrived.
5) Stop the session from your code (e.g., Ctrl+C) or when the item closes,
   which triggers on_closed.
"""

from time import sleep
from threading import Event
from windows_capture import WindowsCapture, Frame, InternalCaptureControl

# Configure capture; choose exactly one of window_name or monitor_index for clarity.
# - To capture a specific window, set window_name to that window's title and leave
#   monitor_index as None.
# - To capture a monitor, set monitor_index (e.g., 0 for primary) and leave
#   window_name as None.
capture = WindowsCapture(
    cursor_capture=False,   # Exclude cursor for slightly higher FPS
    # On some Windows versions, toggling the border is unsupported by the
    # Graphics Capture API. Use None to keep the OS default and avoid toggling.
    draw_border=None,
    monitor_index=None,     # e.g., 0 for primary monitor. Keep None if targeting a window
    window_name=None,       # e.g., "Untitled - Notepad". Keep None if targeting a monitor
)


_stopped = Event()

@capture.event
def on_frame_arrived(frame: Frame, capture_control: InternalCaptureControl):
    """Called on each new frame from the capture thread.

    Keep this handler minimal to maximize FPS. Heavy work (like PNG encoding
    or CPU/GPU processing) should be moved to a separate worker thread/queue.
    """
    # No FPS counting or prints here to avoid overhead.
    # Do the lightest possible per-frame work, or queue frames elsewhere.
    pass


@capture.event
def on_closed():
    """Called when the capture item/session closes."""
    print("Capture Session Closed")
    _stopped.set()


# Start the capture thread. After this, frames begin arriving and invoking
# on_frame_arrived automatically.
capture.start()

# Wait until the capture session signals it has closed (no sleep loop).
try:
    _stopped.wait()
except KeyboardInterrupt:
    stop_fn = getattr(capture, "stop", None)
    if callable(stop_fn):
        try:
            stop_fn()
        except Exception:
            pass
