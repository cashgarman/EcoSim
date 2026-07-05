#!/usr/bin/env python3
"""Kill processes listening on the EcoSim dev server port (default 8765)."""

import argparse
import os
import subprocess
import sys


def default_port() -> int:
    return int(os.environ.get('PORT', '8765'))


def local_port_from_addr(addr: str, port: int) -> bool:
    if addr.startswith('['):
        host, _, p = addr.rpartition(']')
        return p == f':{port}'
    _, _, p = addr.rpartition(':')
    return p == str(port)


def find_listener_pids(port: int) -> list[int]:
    if sys.platform == 'win32':
        return _win_listener_pids(port)
    return _unix_listener_pids(port)


def _win_listener_pids(port: int) -> list[int]:
    out = subprocess.check_output(['netstat', '-ano'], text=True, errors='replace')
    pids: set[int] = set()
    for line in out.splitlines():
        if 'LISTENING' not in line:
            continue
        parts = line.split()
        if len(parts) < 5:
            continue
        if not local_port_from_addr(parts[1], port):
            continue
        try:
            pids.add(int(parts[-1]))
        except ValueError:
            continue
    return sorted(pids)


def _unix_listener_pids(port: int) -> list[int]:
    try:
        out = subprocess.check_output(
            ['lsof', '-nP', f'-iTCP:{port}', '-sTCP:LISTEN', '-t'],
            text=True,
            stderr=subprocess.DEVNULL,
        )
        pids = {int(pid) for pid in out.split() if pid.strip().isdigit()}
        if pids:
            return sorted(pids)
    except (FileNotFoundError, subprocess.CalledProcessError):
        pass

    try:
        out = subprocess.check_output(['ss', '-ltn', f'sport = :{port}'], text=True, errors='replace')
    except (FileNotFoundError, subprocess.CalledProcessError):
        return []

    pids: set[int] = set()
    for line in out.splitlines():
        if 'pid=' not in line:
            continue
        for chunk in line.split(','):
            chunk = chunk.strip()
            if chunk.startswith('pid='):
                pid_str = chunk[4:].split(',', 1)[0]
                if pid_str.isdigit():
                    pids.add(int(pid_str))
    return sorted(pids)


def kill_pid(pid: int) -> None:
    if sys.platform == 'win32':
        subprocess.check_call(['taskkill', '/PID', str(pid), '/F'], stdout=subprocess.DEVNULL)
        return
    os.kill(pid, 15)


def main() -> int:
    parser = argparse.ArgumentParser(description='Stop EcoSim dev server on a TCP port.')
    parser.add_argument(
        '--port',
        type=int,
        default=default_port(),
        help='Port to free (default: PORT env or 8765)',
    )
    args = parser.parse_args()

    pids = find_listener_pids(args.port)
    if not pids:
        print(f'No process listening on port {args.port}.')
        return 0

    killed: list[int] = []
    for pid in pids:
        if pid == os.getpid():
            continue
        try:
            kill_pid(pid)
            killed.append(pid)
            print(f'Stopped PID {pid} on port {args.port}.')
        except (subprocess.CalledProcessError, ProcessLookupError, PermissionError) as err:
            print(f'Failed to stop PID {pid}: {err}', file=sys.stderr)

    if not killed:
        return 1
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
