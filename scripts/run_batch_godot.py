#!/usr/bin/env python3
"""
Run headless batch ecology tests via the C# EcoSim.BatchCli (no browser).

Examples:
  python scripts/run_batch_godot.py --seed 1 --size s --days 100
  python scripts/run_batch_godot.py --days 50 --sample-every 10
"""

from __future__ import annotations

import argparse
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DOTNET_ROOT = ROOT / '.dotnet'
DOTNET = DOTNET_ROOT / 'dotnet.exe' if (DOTNET_ROOT / 'dotnet.exe').is_file() else 'dotnet'
CLI_PROJECT = ROOT / 'EcoSim.BatchCli' / 'EcoSim.BatchCli.csproj'
REPORTS_DIR = ROOT / 'reports'
DEFAULT_GODOT = Path(r'C:\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe')


def configure_env() -> dict[str, str]:
    env = os.environ.copy()
    if DOTNET_ROOT.is_dir():
        env['DOTNET_ROOT'] = str(DOTNET_ROOT)
        env['PATH'] = str(DOTNET_ROOT) + os.pathsep + env.get('PATH', '')
    return env


def main() -> int:
    parser = argparse.ArgumentParser(description='EcoSim Godot/C# headless batch runner')
    parser.add_argument('--seed', type=int, default=1)
    parser.add_argument('--size', default='s', choices=['s', 'm', 'l', 'xl', 'xxl'])
    parser.add_argument('--days', type=int, default=100)
    parser.add_argument('--sample-every', type=int, default=10)
    parser.add_argument('--report-dir', default=str(REPORTS_DIR))
    parser.add_argument('--auto-migration', action='store_true')
    parser.add_argument('--max-wall-ms', type=int, default=120_000)
    parser.add_argument('--balance-file', default=None)
    args = parser.parse_args()

    env = configure_env()
    cmd = [
        str(DOTNET),
        'run',
        '--project',
        str(CLI_PROJECT),
        '--',
        '--data-root',
        str(ROOT),
        '--seed',
        str(args.seed),
        '--size',
        args.size,
        '--days',
        str(args.days),
        '--sample-every',
        str(args.sample_every),
        '--report-dir',
        args.report_dir,
    ]
    if args.auto_migration:
        cmd.append('--auto-migration')
    if args.max_wall_ms:
        cmd.extend(['--max-wall-ms', str(args.max_wall_ms)])
    if args.balance_file:
        cmd.extend(['--balance-file', args.balance_file])

    godot_bin = os.environ.get('GODOT_BIN', str(DEFAULT_GODOT))
    if Path(godot_bin).is_file():
        print(f'Godot binary: {godot_bin}', flush=True)

    print(' '.join(cmd), flush=True)
    result = subprocess.run(cmd, cwd=ROOT, env=env, check=False)
    return result.returncode


if __name__ == '__main__':
    raise SystemExit(main())
