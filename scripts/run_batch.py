#!/usr/bin/env python3
"""
EcoSim batch ecology test CLI (headless browser driver + live terminal UI).

Setup:
  python -m pip install -r scripts/requirements-batch.txt
  python -m playwright install chromium

Examples:
  python scripts/run_batch.py --days 100 --size s
  python scripts/run_batch_gpu.py --days 100 --size s   # WebGPU (headed on Windows)
  python scripts/run_batch.py --days 50 --plain
"""

from __future__ import annotations

import argparse
import base64
import json
import mimetypes
import os
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from urllib.parse import urlparse
ROOT = Path(__file__).resolve().parents[1]

REPORTS_DIR = ROOT / 'reports'
DEFAULT_PORT = 8765
IS_WINDOWS = sys.platform == 'win32'


def configure_stdio() -> None:
    """Use UTF-8 on Windows so Rich spinners and separators render correctly."""
    if not IS_WINDOWS:
        return
    os.environ.setdefault('PYTHONIOENCODING', 'utf-8')
    for stream in (sys.stdout, sys.stderr):
        reconfigure = getattr(stream, 'reconfigure', None)
        if reconfigure:
            try:
                reconfigure(encoding='utf-8')
            except Exception:
                pass


@dataclass
class RunConfig:
    port: int = 8765
    seed: int = 42
    size: str = 's'
    days: int = 100
    sample_every: int = 10
    animals: float = 0.45
    runs: int = 1
    sim: str = 'cpu'
    auto_migration: bool = False
    balance_file: Path | None = None
    fuzz: bool = False
    fuzz_trials: int = 50
    fuzz_seed: int = 12345
    fuzz_intensity: float = 0.15
    fuzz_scope: str = 'all'
    fuzz_profile: str = 'fast'
    timeout: int = 900
    plain: bool = False
    headed: bool = False
    headless: bool = False


@dataclass
class ProgressSnapshot:
    phase: str = 'starting'
    mode: str = 'single'
    day: int = 0
    target_days: int = 1
    total_alive: int = 0
    generation_max: int = 0
    wall_ms: float = 0
    trial_index: int = 0
    trial_total: int = 1
    run_index: int = 0
    run_total: int = 1
    message: str = ''

    @classmethod
    def from_js(cls, raw: dict | None) -> ProgressSnapshot:
        if not raw:
            return cls()
        return cls(
            phase=str(raw.get('phase') or 'starting'),
            mode=str(raw.get('mode') or 'single'),
            day=int(raw.get('day') or 0),
            target_days=max(1, int(raw.get('targetDays') or 1)),
            total_alive=int(raw.get('totalAlive') or 0),
            generation_max=int(raw.get('generationMax') or 0),
            wall_ms=float(raw.get('wallMs') or 0),
            trial_index=int(raw.get('trialIndex') or 0),
            trial_total=max(1, int(raw.get('trialTotal') or 1)),
            run_index=int(raw.get('runIndex') or 0),
            run_total=max(1, int(raw.get('runTotal') or 1)),
            message=str(raw.get('message') or ''),
        )


def server_alive(port: int) -> bool:
    try:
        with urllib.request.urlopen(f'http://127.0.0.1:{port}/batch-test.html', timeout=2) as resp:
            return resp.status == 200
    except (urllib.error.URLError, TimeoutError):
        return False



def server_api_healthy(port: int) -> bool:
    try:
        with urllib.request.urlopen(f'http://127.0.0.1:{port}/api/batch-reports', timeout=3) as resp:
            return resp.status == 200
    except (urllib.error.URLError, TimeoutError, OSError):
        return False


def spawn_batch_server(port: int) -> subprocess.Popen:
    proc = subprocess.Popen(
        [sys.executable, str(ROOT / 'serve.py')],
        cwd=str(ROOT),
        env={**os.environ, 'PORT': str(port), 'BATCH_SERVER': '1'},
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    for _ in range(40):
        if server_alive(port) and server_api_healthy(port):
            return proc
        time.sleep(0.25)
    proc.kill()
    raise RuntimeError('Failed to start EcoSim dev server')


def ensure_batch_server(port: int) -> tuple[subprocess.Popen | None, int]:
    """Static assets are served via Playwright routes; only the report API needs HTTP."""
    if server_alive(port) and server_api_healthy(port):
        return None, port

    if server_alive(port):
        fallback = find_free_port()
        print(
            f'Port {port} is in use — starting dedicated batch API server on port {fallback}…',
            file=sys.stderr,
        )
        return spawn_batch_server(fallback), fallback

    proc = spawn_batch_server(port)
    return proc, port


def encode_balance_file(path: Path) -> str:
    data = json.loads(path.read_text(encoding='utf-8'))
    raw = json.dumps(data).encode('utf-8')
    return base64.urlsafe_b64encode(raw).decode('ascii').rstrip('=')


def build_url(cfg: RunConfig, *, autostart: bool = True) -> str:
    params = [
        f'autostart={1 if autostart else 0}',
        'saveServer=1',
        f'seed={cfg.seed}',
        f'size={cfg.size}',
        f'days={cfg.days}',
        f'sampleEvery={cfg.sample_every}',
        f'animals={cfg.animals}',
        f'runs={cfg.runs}',
        f'sim={cfg.sim}',
        f'autoMigration={1 if cfg.auto_migration else 0}',
    ]
    if cfg.fuzz:
        params.extend([
            'fuzz=1',
            f'fuzzTrials={cfg.fuzz_trials}',
            f'fuzzSeed={cfg.fuzz_seed}',
            f'fuzzIntensity={cfg.fuzz_intensity}',
            f'fuzzScope={cfg.fuzz_scope}',
            f'fuzzProfile={cfg.fuzz_profile}',
        ])
    if cfg.balance_file:
        params.append(f'balance={encode_balance_file(cfg.balance_file)}')
    return f'http://127.0.0.1:{cfg.port}/batch-test.html?' + '&'.join(params)


POLL_JS = '''() => ({
  progress: window.__BATCH_PROGRESS__ || null,
  done: document.getElementById('batch-status')?.dataset?.done === '1',
  batch: window.__BATCH_COMPLETE__ || null,
  fuzz: window.__FUZZ_CAMPAIGN_COMPLETE__ || null,
  statusText: document.getElementById('batch-status')?.textContent || '',
  failed: window.__BATCH_PROGRESS__?.phase === 'error'
})'''


def wants_gpu(cfg: RunConfig) -> bool:
    return cfg.sim == 'gpu' or (cfg.fuzz and cfg.fuzz_profile.endswith('-gpu'))


def find_chrome_executable() -> str | None:
    roots = [
        os.environ.get('PROGRAMFILES', ''),
        os.environ.get('PROGRAMFILES(X86)', ''),
        os.environ.get('LOCALAPPDATA', ''),
    ]
    rels = [
        os.path.join('Google', 'Chrome', 'Application', 'chrome.exe'),
        os.path.join('Microsoft', 'Edge', 'Application', 'msedge.exe'),
    ]
    for root in roots:
        if not root:
            continue
        for rel in rels:
            path = os.path.join(root, rel)
            if os.path.isfile(path):
                return path
    return None


def find_free_port() -> int:
    import socket
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(('127.0.0.1', 0))
        return sock.getsockname()[1]


def wait_for_cdp(port: int, timeout_sec: float = 15.0) -> bool:
    deadline = time.time() + timeout_sec
    url = f'http://127.0.0.1:{port}/json/version'
    while time.time() < deadline:
        try:
            with urllib.request.urlopen(url, timeout=1) as resp:
                if resp.status == 200:
                    return True
        except (urllib.error.URLError, TimeoutError):
            time.sleep(0.25)
    return False


def stop_chrome_proc(proc: subprocess.Popen | None, profile_dir: str | None = None) -> None:
    if proc is None:
        return
    proc.terminate()
    try:
        proc.wait(timeout=8)
    except subprocess.TimeoutExpired:
        proc.kill()
        proc.wait(timeout=3)
    if profile_dir and os.path.isdir(profile_dir):
        try:
            shutil.rmtree(profile_dir, ignore_errors=True)
        except OSError:
            pass


def launch_gpu_cdp_browser(p, cfg: RunConfig):
    """Playwright launch() blocks WebGPU adapters; connect via CDP to real Chrome/Edge."""
    exe = find_chrome_executable()
    if not exe:
        raise RuntimeError('GPU batch requires Google Chrome or Microsoft Edge installed')

    port = find_free_port()
    profile_parent = ROOT / '.batch-gpu-profile'
    profile_parent.mkdir(exist_ok=True)
    profile_dir = tempfile.mkdtemp(prefix='run-', dir=profile_parent)

    chrome_args = [
        exe,
        f'--remote-debugging-port={port}',
        f'--user-data-dir={profile_dir}',
        '--no-first-run',
        '--no-default-browser-check',
        '--enable-unsafe-webgpu',
    ]
    if not cfg.headed:
        chrome_args.append('--headless=new')

    proc = subprocess.Popen(
        chrome_args,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    if not wait_for_cdp(port):
        stop_chrome_proc(proc, profile_dir)
        raise RuntimeError('Failed to start Chrome/Edge for GPU batch (CDP timeout)')

    browser = p.chromium.connect_over_cdp(f'http://127.0.0.1:{port}')
    return browser, proc, profile_dir


def launch_batch_browser(p, cfg: RunConfig):
    """Launch browser for batch runs. GPU uses CDP-attached Chrome/Edge for WebGPU."""
    if wants_gpu(cfg):
        return launch_gpu_cdp_browser(p, cfg)

    browser = p.chromium.launch(headless=not cfg.headed)
    return browser, None, None


def new_batch_page(browser):
    context = browser.contexts[0] if browser.contexts else browser.new_context()
    return context.new_page()


def install_batch_static_routes(page, port: int) -> None:
    """Serve JS/JSON/HTML from disk — avoids flaky parallel fetches through serve.py."""
    root = ROOT.resolve()
    pattern = f'http://127.0.0.1:{port}/**'

    def handler(route):
        request = route.request
        if request.method != 'GET':
            route.continue_()
            return
        parsed = urlparse(request.url)
        if parsed.path.startswith('/api/'):
            route.continue_()
            return
        rel = parsed.path.lstrip('/')
        if not rel:
            route.continue_()
            return
        file_path = (root / rel).resolve()
        try:
            file_path.relative_to(root)
        except ValueError:
            route.fulfill(status=403, body=b'Forbidden')
            return
        if not file_path.is_file():
            route.continue_()
            return
        data = file_path.read_bytes()
        ctype = mimetypes.guess_type(str(file_path))[0] or 'application/octet-stream'
        if file_path.suffix in ('.js', '.mjs'):
            ctype = 'text/javascript; charset=utf-8'
        elif file_path.suffix == '.json':
            ctype = 'application/json; charset=utf-8'
        elif file_path.suffix == '.html':
            ctype = 'text/html; charset=utf-8'
        route.fulfill(status=200, body=data, headers={
            'Content-Type': ctype,
            'Cache-Control': 'no-store',
        })

    page.route(pattern, handler)


def load_batch_page(page, url: str) -> None:
    failed_requests: list = []

    def on_failed(request):
        failure = request.failure
        failed_requests.append({
            'url': request.url,
            'err': failure if isinstance(failure, str) else getattr(failure, 'error_text', str(failure)),
        })

    page.on('requestfailed', on_failed)
    last_err = None
    for attempt in range(4):
        failed_requests.clear()
        if attempt > 0:
            page.goto('about:blank', wait_until='domcontentloaded', timeout=30000)
            time.sleep(0.5 * attempt)
        page.goto(url, wait_until='domcontentloaded', timeout=120000)
        try:
            wait_ms = min(20000 + attempt * 15000, 90000)
            page.wait_for_function(
                '''() => window.batchTestApp != null
                  || (document.getElementById('batch-status')?.textContent || '').startsWith('Init error:')''',
                timeout=wait_ms,
            )
            status = page.evaluate('''() => document.getElementById('batch-status')?.textContent || '' ''')
            if status.startswith('Init error:'):
                raise RuntimeError(status)
            return
        except Exception as err:
            last_err = err
            if attempt < 3:
                continue
    raise RuntimeError(
        'Batch page JavaScript did not load (window.batchTestApp missing).\n'
        f'Failed requests: {failed_requests[-5:]}\n'
        'Stop any broken server on port 8765 and run: python serve.py'
    ) from last_err


def format_batch_failure(msg: str, cfg: RunConfig) -> str:
    if not wants_gpu(cfg):
        return msg
    if 'dxil.dll' in msg or 'no-adapter' in msg or 'requestDevice' in msg or 'no-webgpu' in msg:
        return (
            f'{msg}\n\n'
            'GPU batch needs Chrome/Edge with WebGPU. The CLI now launches Chrome via CDP for this.\n'
            'Ensure Google Chrome or Microsoft Edge is installed, then retry:\n'
            '  python scripts/run_batch_gpu.py --days 5 --size s\n'
            'Or use batch-test.html in a normal browser with sim=gpu.'
        )
    return msg


class PlainUi:
    def __init__(self, cfg: RunConfig):
        self.cfg = cfg
        self._last_line = ''

    def __enter__(self):
        mode = 'fuzz' if self.cfg.fuzz else f'{self.cfg.runs} run(s)'
        print(f'EcoSim Batch | seed={self.cfg.seed} size={self.cfg.size} days={self.cfg.days} | {mode}')
        return self

    def __exit__(self, *args):
        print()

    def tick(self, snap: ProgressSnapshot):
        line = snap.message or f'day {snap.day}/{snap.target_days} pop {snap.total_alive}'
        if line != self._last_line:
            print(f'  {line}')
            self._last_line = line

    def finish(self, result: dict):
        print_summary(result, plain=True)
        print(f'Reports saved under {REPORTS_DIR}')

    def fail(self, message: str):
        print(f'Error: {message}', file=sys.stderr)


class RichUi:
    def __init__(self, cfg: RunConfig):
        self.cfg = cfg
        self.snap = ProgressSnapshot(
            target_days=cfg.days,
            trial_total=cfg.fuzz_trials if cfg.fuzz else 1,
            run_total=cfg.runs,
        )
        self._live = None
        self._console = None
        self._page_errors: list[str] = []

    def __enter__(self):
        from rich.console import Console
        from rich.live import Live

        self._console = Console(force_terminal=True, legacy_windows=False)
        self._live = Live(
            self.render(),
            console=self._console,
            auto_refresh=False,
            transient=False,
        )
        self._live.__enter__()
        return self

    def __exit__(self, *args):
        self.stop()

    def stop(self):
        if self._live:
            try:
                self._live.stop()
            except Exception:
                pass
            self._live = None

    def note_page_error(self, message: str):
        self._page_errors.append(message)
        if len(self._page_errors) > 3:
            self._page_errors.pop(0)

    def tick(self, snap: ProgressSnapshot):
        self.snap = snap
        if self._live:
            self._live.update(self.render())

    def finish(self, result: dict):
        self.stop()
        print_summary(result, plain=False, console=self._console)
        self._console.print(f'[dim]Reports saved under {REPORTS_DIR}[/dim]')

    def fail(self, message: str):
        self.snap.message = message
        if self._live:
            self._live.update(self.render())
        self.stop()
        self._console.print(f'[bold red]{message}[/bold red]')

    def render(self):
        from rich.panel import Panel
        from rich.progress import BarColumn, Progress, SpinnerColumn, TextColumn, TimeElapsedColumn
        from rich.table import Table
        from rich import box
        from rich.console import Group

        cfg = self.cfg
        snap = self.snap

        header = Table.grid(padding=(0, 2))
        header.add_column(style='bold cyan')
        header.add_column()
        header.add_row('EcoSim Batch Runner', f'{cfg.sim.upper()} ecology simulation')
        if cfg.headed:
            header.add_row('Browser', 'headed (Windows WebGPU)')
        header.add_row('Seed', str(cfg.seed))
        header.add_row('World', f'{cfg.size} | {cfg.days} sim-days | {cfg.sim}')
        if cfg.fuzz:
            header.add_row('Fuzz', f'{cfg.fuzz_trials} trials @ ±{cfg.fuzz_intensity:.0%} ({cfg.fuzz_profile})')
        elif cfg.runs > 1:
            header.add_row('Sweep', f'{cfg.runs} sequential runs')

        stats = Table(box=box.SIMPLE, show_header=True, header_style='bold')
        stats.add_column('Metric', style='dim')
        stats.add_column('Value', justify='right')
        stats.add_row('Phase', snap.phase)
        if snap.mode == 'fuzz':
            stats.add_row('Trial', f'{snap.trial_index + 1} / {snap.trial_total}')
        elif snap.run_total > 1:
            stats.add_row('Run', f'{snap.run_index + 1} / {snap.run_total}')
        stats.add_row('Sim day', f'{snap.day} / {snap.target_days}')
        stats.add_row('Population', f'{snap.total_alive:,}')
        stats.add_row('Max generation', str(snap.generation_max))
        stats.add_row('Elapsed', f'{snap.wall_ms / 1000:.1f}s')
        if snap.mode == 'fuzz' and snap.trial_index > 0 and snap.wall_ms > 0:
            tpm = (snap.trial_index / (snap.wall_ms / 60000))
            stats.add_row('Trials/min', f'{tpm:.1f}')

        day_pct = min(100, int(100 * snap.day / max(1, snap.target_days)))
        if snap.mode == 'fuzz':
            outer_pct = int(100 * snap.trial_index / max(1, snap.trial_total))
            outer_label = f'Trial {snap.trial_index + 1}/{snap.trial_total}'
        elif snap.run_total > 1:
            outer_pct = int(100 * snap.run_index / max(1, snap.run_total))
            outer_label = f'Run {snap.run_index + 1}/{snap.run_total}'
        else:
            outer_pct = day_pct
            outer_label = 'Overall'

        progress = Progress(
            TextColumn('[bold blue]{task.description}'),
            BarColumn(bar_width=40),
            TextColumn('{task.percentage:>3.0f}%'),
            TimeElapsedColumn(),
        )
        if not IS_WINDOWS:
            progress = Progress(
                SpinnerColumn(),
                TextColumn('[bold blue]{task.description}'),
                BarColumn(bar_width=40),
                TextColumn('{task.percentage:>3.0f}%'),
                TimeElapsedColumn(),
            )
        progress.add_task(outer_label, total=100, completed=outer_pct)
        progress.add_task(f'Sim day {snap.day}/{snap.target_days}', total=100, completed=day_pct)

        footer = snap.message or 'Waiting for simulation...'
        if self._page_errors:
            footer += f'\nPage error: {self._page_errors[-1]}'
        body = Group(
            Panel(header, title='Configuration', border_style='cyan'),
            Panel(stats, title='Live metrics', border_style='green'),
            Panel(progress, title='Progress', border_style='blue'),
            Panel(footer, title='Status', border_style='dim'),
        )
        return body

    def render_done(self, result: dict):
        from rich.panel import Panel
        from rich.text import Text

        if result.get('fuzz'):
            campaign = result['fuzz']
            hist = campaign.get('histogram') or {}
            txt = Text.assemble(
                ('Campaign complete\n', 'bold green'),
                (f"ID: {campaign.get('campaignId')}\n", 'dim'),
                (f"Stable: {hist.get('stable', 0)}  ", 'green'),
                (f"Partial: {hist.get('partial_collapse', 0)}  ", 'yellow'),
                (f"Extinct: {hist.get('total_extinction', 0)}\n", 'red'),
                (f"{campaign.get('trialsPerMinute', 0):.1f} trials/min", 'cyan'),
            )
        else:
            report = result.get('batch') or {}
            summary = report.get('summary') or {}
            txt = Text.assemble(
                ('Run complete\n', 'bold green'),
                (f"Outcome: {report.get('outcome')}\n", 'white'),
                (f"Population: {summary.get('finalPop')}  ", 'cyan'),
                (f"Generation: {summary.get('generationMax')}\n", 'cyan'),
                (f"Report: {report.get('runId')}", 'dim'),
            )
        return Panel(txt, title='Finished', border_style='green')


def default_progress(cfg: RunConfig) -> ProgressSnapshot:
    return ProgressSnapshot(
        target_days=cfg.days,
        trial_total=cfg.fuzz_trials if cfg.fuzz else 1,
        run_total=cfg.runs,
        message='Loading batch runner...',
    )


def run_playwright(url: str, timeout_sec: int, cfg: RunConfig) -> dict:
    try:
        from playwright.sync_api import sync_playwright
    except ImportError as err:
        raise SystemExit(
            'Playwright not installed. Run:\n'
            '  python -m pip install -r scripts/requirements-batch.txt\n'
            '  python -m playwright install chromium'
        ) from err

    ui = PlainUi(cfg) if cfg.plain else None
    rich_ui = None
    if not cfg.plain:
        try:
            rich_ui = RichUi(cfg)
            ui = rich_ui
        except ImportError:
            ui = PlainUi(cfg)

    with sync_playwright() as p:
        cli_t0 = time.time()
        browser, chrome_proc, chrome_profile = launch_batch_browser(p, cfg)
        page = new_batch_page(browser)
        install_batch_static_routes(page, cfg.port)
        page.on('pageerror', lambda err: ui.note_page_error(str(err)) if hasattr(ui, 'note_page_error') else None)
        try:
            gpu_mode = wants_gpu(cfg)
            url = build_url(cfg, autostart=not gpu_mode)
            load_batch_page(page, url)
            if gpu_mode:
                probe = page.evaluate('''async () => ({
                  hasGpu: !!navigator.gpu,
                  adapter: !!(navigator.gpu && await navigator.gpu.requestAdapter()),
                })''')
                if not probe.get('adapter'):
                    msg = format_batch_failure('GPU backend unavailable (no-adapter)', cfg)
                    raise RuntimeError(msg)
                page.evaluate('() => window.batchTestApp.startRun()')
            page.wait_for_function(
                '''() => window.__BATCH_PROGRESS__ != null
                  || document.getElementById('batch-status')?.dataset?.done === '1'
                  || (document.getElementById('batch-status')?.textContent || '').startsWith('Error:')
                  || (document.getElementById('batch-status')?.textContent || '').startsWith('Init error:')''',
                timeout=120000,
            )
            with ui:
                deadline = time.time() + timeout_sec
                result = None
                while time.time() < deadline:
                    payload = page.evaluate(POLL_JS)
                    snap = ProgressSnapshot.from_js(payload.get('progress'))
                    if snap.target_days <= 1 and cfg.days > 1:
                        snap.target_days = cfg.days
                    if payload.get('statusText') and not snap.message:
                        snap.message = payload['statusText']
                    if not payload.get('progress'):
                        snap = default_progress(cfg)
                    ui.tick(snap)
                    if payload.get('done'):
                        status = payload.get('statusText') or ''
                        if (
                            payload.get('failed')
                            or status.startswith('Error:')
                            or status.startswith('Init error:')
                        ):
                            msg = format_batch_failure(status or 'Batch run failed', cfg)
                            ui.fail(msg)
                            raise RuntimeError(msg)
                        result = {
                            'batch': payload.get('batch'),
                            'fuzz': payload.get('fuzz'),
                            'cliTotalMs': int((time.time() - cli_t0) * 1000),
                        }
                        break
                    time.sleep(0.2)
                if result is None:
                    ui.fail(f'Batch run timed out after {timeout_sec}s')
                    raise RuntimeError(f'Batch run timed out after {timeout_sec}s')
                ui.finish(result)
                return result
        finally:
            browser.close()
            stop_chrome_proc(chrome_proc, chrome_profile)


def print_summary(result: dict, plain: bool = True, console=None):
    if result.get('fuzz'):
        campaign = result['fuzz']
        hist = campaign.get('histogram') or {}
        if plain:
            print(f"\nCampaign {campaign.get('campaignId')} — {campaign.get('fuzzTrials')} trials")
            print(f"  stable={hist.get('stable',0)} partial={hist.get('partial_collapse',0)} extinct={hist.get('total_extinction',0)}")
            print(f"  {campaign.get('trialsPerMinute',0):.1f} trials/min")
            print('Top configs:')
            for row in (campaign.get('ranked') or [])[:5]:
                print(f"  score={row.get('score',0):.2f} outcome={row.get('outcome')} pop={row.get('finalPop')} runId={row.get('runId')}")
        else:
            from rich.table import Table

            table = Table(title='Top fuzz trials', show_lines=True)
            table.add_column('Rank', justify='right')
            table.add_column('Score', justify='right')
            table.add_column('Outcome')
            table.add_column('Pop', justify='right')
            table.add_column('Gen', justify='right')
            table.add_column('Run ID')
            for i, row in enumerate((campaign.get('ranked') or [])[:10], 1):
                table.add_row(
                    str(i),
                    f"{row.get('score', 0):.2f}",
                    str(row.get('outcome')),
                    str(row.get('finalPop')),
                    str(row.get('generationMax')),
                    str(row.get('runId')),
                )
            console.print()
            console.print(table)
        if hist.get('stable', 0) == 0 and hist.get('partial_collapse', 0) == 0:
            sys.exit(1)
        return

    report = result.get('batch')
    if not report:
        print('No report returned')
        sys.exit(1)
    summary = report.get('summary') or {}
    if plain:
        cli_ms = result.get('cliTotalMs')
        cli_note = f' cli={cli_ms / 1000:.1f}s' if cli_ms else ''
        print(f"\nRun {report.get('runId')} — {report.get('outcome')} pop={summary.get('finalPop')} gen={summary.get('generationMax')} wall={report.get('wallMs', 0) / 1000:.1f}s{cli_note}")
    else:
        from rich.table import Table

        table = Table(title='Run summary', show_header=False)
        table.add_row('Run ID', str(report.get('runId')))
        table.add_row('Outcome', str(report.get('outcome')))
        table.add_row('Final population', str(summary.get('finalPop')))
        table.add_row('Max generation', str(summary.get('generationMax')))
        table.add_row('Peak population', str(summary.get('peakPop')))
        table.add_row('Wall time', f"{report.get('wallMs', 0) / 1000:.1f}s")
        cli_ms = result.get('cliTotalMs')
        if cli_ms:
            table.add_row('CLI total', f"{cli_ms / 1000:.1f}s")
        console.print()
        console.print(table)
    if report.get('outcome') == 'total_extinction':
        sys.exit(1)


def parse_args() -> RunConfig:
    parser = argparse.ArgumentParser(description='Run EcoSim batch ecology tests headlessly')
    parser.add_argument('--port', type=int, default=DEFAULT_PORT)
    parser.add_argument('--seed', type=int, default=42)
    parser.add_argument('--size', default='s')
    parser.add_argument('--days', type=int, default=100)
    parser.add_argument('--sample-every', type=int, default=10)
    parser.add_argument('--animals', type=float, default=0.45)
    parser.add_argument('--runs', type=int, default=1)
    parser.add_argument('--sim', default='cpu', choices=['cpu', 'gpu'])
    parser.add_argument('--gpu', action='store_true', help='Shorthand for --sim gpu')
    parser.add_argument('--auto-migration', action='store_true')
    parser.add_argument('--balance-file', type=Path)
    parser.add_argument('--fuzz', action='store_true')
    parser.add_argument('--fuzz-trials', type=int, default=50)
    parser.add_argument('--fuzz-seed', type=int, default=12345)
    parser.add_argument('--fuzz-intensity', type=float, default=0.15)
    parser.add_argument('--fuzz-scope', default='all')
    parser.add_argument('--fuzz-profile', default='fast', choices=['fast', 'deep', 'fast-gpu', 'deep-gpu'])
    parser.add_argument('--timeout', type=int, default=900)
    parser.add_argument('--plain', action='store_true', help='Disable Rich live UI (plain log lines)')
    parser.add_argument('--headed', action='store_true', help='Show browser window (default for GPU on Windows)')
    parser.add_argument('--headless', action='store_true', help='Force headless even for GPU (may fail on Windows)')
    args = parser.parse_args()
    if args.gpu:
        args.sim = 'gpu'
    timeout = args.timeout
    if args.sim == 'gpu' and timeout == 900:
        timeout = 1200
    return RunConfig(
        port=args.port,
        seed=args.seed,
        size=args.size,
        days=args.days,
        sample_every=args.sample_every,
        animals=args.animals,
        runs=args.runs,
        sim=args.sim,
        auto_migration=args.auto_migration,
        balance_file=args.balance_file,
        fuzz=args.fuzz,
        fuzz_trials=args.fuzz_trials,
        fuzz_seed=args.fuzz_seed,
        fuzz_intensity=args.fuzz_intensity,
        fuzz_scope=args.fuzz_scope,
        fuzz_profile=args.fuzz_profile,
        timeout=timeout,
        plain=args.plain,
        headed=args.headed,
        headless=args.headless,
    )


def main():
    configure_stdio()
    cfg = parse_args()
    REPORTS_DIR.mkdir(exist_ok=True)
    proc, port = ensure_batch_server(cfg.port)
    cfg.port = port
    try:
        url = build_url(cfg, autostart=not wants_gpu(cfg))
        run_playwright(url, cfg.timeout, cfg)
    finally:
        if proc is not None:
            proc.terminate()


if __name__ == '__main__':
    main()
