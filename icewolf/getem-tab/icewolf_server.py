"""Icewolf Getem: starts Getem as a local server for the browser tab.
pythonw has no console, so every message and error goes to .cache/server-log.txt -
Icewolf shows the error from there if Getem cannot start.

--browser-pid N   Getem stops as soon as that Icewolf process has closed."""
import os
import sys
import traceback
from pathlib import Path

ROOT = Path(__file__).resolve().parent
(ROOT / ".cache").mkdir(exist_ok=True)
_log = open(ROOT / ".cache" / "server-log.txt", "w", encoding="utf-8", buffering=1)
sys.stdout = sys.stderr = _log


def _browser_pid():
    args = sys.argv[1:]
    for i, a in enumerate(args):
        if a == "--browser-pid" and i + 1 < len(args) and args[i + 1].isdigit():
            pid = int(args[i + 1])
            return pid if 0 < pid < 2 ** 32 else None
    return None


def _kill_children_with_us():
    """Puts this process into a Windows job object that ends every helper program it starts
    (yt-dlp's ffmpeg etc.) when Getem ends - nothing is left running in the background."""
    if os.name != "nt":
        return
    try:
        import ctypes
        from ctypes import wintypes
        k32 = ctypes.WinDLL("kernel32", use_last_error=True)

        class IO_COUNTERS(ctypes.Structure):
            _fields_ = [(n, ctypes.c_ulonglong) for n in ("r", "w", "o", "rb", "wb", "ob")]

        class BASIC(ctypes.Structure):
            _fields_ = [("PerProcessUserTimeLimit", ctypes.c_longlong), ("PerJobUserTimeLimit", ctypes.c_longlong),
                        ("LimitFlags", wintypes.DWORD), ("MinimumWorkingSetSize", ctypes.c_size_t),
                        ("MaximumWorkingSetSize", ctypes.c_size_t), ("ActiveProcessLimit", wintypes.DWORD),
                        ("Affinity", ctypes.c_size_t), ("PriorityClass", wintypes.DWORD),
                        ("SchedulingClass", wintypes.DWORD)]

        class EXTENDED(ctypes.Structure):
            _fields_ = [("BasicLimitInformation", BASIC), ("IoInfo", IO_COUNTERS),
                        ("ProcessMemoryLimit", ctypes.c_size_t), ("JobMemoryLimit", ctypes.c_size_t),
                        ("PeakProcessMemoryUsed", ctypes.c_size_t), ("PeakJobMemoryUsed", ctypes.c_size_t)]

        k32.CreateJobObjectW.restype = wintypes.HANDLE
        k32.CreateJobObjectW.argtypes = [wintypes.LPVOID, wintypes.LPCWSTR]
        k32.SetInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, wintypes.LPVOID, wintypes.DWORD]
        k32.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
        k32.GetCurrentProcess.restype = wintypes.HANDLE

        job = k32.CreateJobObjectW(None, None)
        if not job:
            return
        info = EXTENDED()
        info.BasicLimitInformation.LimitFlags = 0x2000          # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if not k32.SetInformationJobObject(job, 9, ctypes.byref(info), ctypes.sizeof(info)):   # 9 = extended limits
            return
        if k32.AssignProcessToJobObject(job, k32.GetCurrentProcess()):
            globals()["_JOB"] = job          # keep the handle open for the lifetime of this process
            print("helper programs end together with Getem")
    except Exception as e:                   # never stop Getem because of this
        print("job object not available:", e)


try:
    _kill_children_with_us()
    sys.path.insert(0, str(ROOT))
    import app
    import server
    server.run(app.Api(), app.ROOT, browser_pid=_browser_pid())
except SystemExit:
    raise
except BaseException:
    print("GETEM-SERVER-FAILED")
    traceback.print_exc()
    _log.flush()
    sys.exit(1)
