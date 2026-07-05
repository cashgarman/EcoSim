#!/usr/bin/env python3
"""
Run batch ecology tests on the WebGPU simulation backend.

On Windows, headed Chrome/Edge is used by default (headless Chromium cannot load WebGPU/dxil.dll).
"""

import runpy
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.argv[0] = str(ROOT / 'scripts' / 'run_batch.py')
if '--sim' not in sys.argv and '--gpu' not in sys.argv:
    sys.argv.insert(1, '--sim')
    sys.argv.insert(2, 'gpu')

runpy.run_path(str(ROOT / 'scripts' / 'run_batch.py'), run_name='__main__')
