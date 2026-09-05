#!/usr/bin/env python3
"""Bound a local CLI and its descendants without depending on GNU timeout."""
import os
import signal
import subprocess
import sys


def main():
    timeout = int(sys.argv[1])
    if timeout <= 0 or len(sys.argv) < 3:
        return 124
    process = subprocess.Popen(sys.argv[2:], start_new_session=True)

    def terminate(*_):
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        process.wait()
        raise SystemExit(124)

    signal.signal(signal.SIGTERM, terminate)
    signal.signal(signal.SIGINT, terminate)
    try:
        return process.wait(timeout=timeout)
    except subprocess.TimeoutExpired:
        terminate()


if __name__ == "__main__":
    sys.exit(main())
