# Sends key presses straight to one X11 window (no focus change), for checking the game UI.
# Usage: python3 tools/scripts/sendkey.py <window-id-hex> <keysym>...   e.g. 0x5a0000a f Tab Escape
# Find the id with: xwininfo -root -tree | grep "\"Roguelike\""
import sys, time
from Xlib import X, XK, display, protocol
d = display.Display()
win = d.create_resource_object('window', int(sys.argv[1], 16))
for name in sys.argv[2:]:
    code = d.keysym_to_keycode(XK.string_to_keysym(name))
    for typ, cls in ((X.KeyPress, protocol.event.KeyPress), (X.KeyRelease, protocol.event.KeyRelease)):
        ev = cls(time=X.CurrentTime, root=d.screen().root, window=win, same_screen=1, child=X.NONE,
                 root_x=0, root_y=0, event_x=0, event_y=0, state=0, detail=code)
        win.send_event(ev, propagate=True)
        d.flush()
        time.sleep(0.08)
    time.sleep(0.25)
