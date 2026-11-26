"""Icewolf GetemTab: starts Getem as a local server for the browser tab.
pythonw has no console, so every message and error goes to .cache/server-log.txt -
Icewolf shows the error from there if Getem cannot start."""
import sys
import traceback
from pathlib import Path

ROOT = Path(__file__).resolve().parent
(ROOT / ".cache").mkdir(exist_ok=True)
_log = open(ROOT / ".cache" / "server-log.txt", "w", encoding="utf-8", buffering=1)
sys.stdout = sys.stderr = _log

try:
    sys.path.insert(0, str(ROOT))
    import app
    import server
    server.run(app.Api(), app.ROOT)
except SystemExit:
    raise
except BaseException:
    print("GETEM-SERVER-FAILED")
    traceback.print_exc()
    _log.flush()
    sys.exit(1)
