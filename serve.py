#!/usr/bin/env python3
"""Threaded static file server for EcoSim ES module development + batch report API."""

from http.server import HTTPServer, ThreadingHTTPServer, SimpleHTTPRequestHandler
import json
import os
import re
import threading
import time


REPORTS_DIR = 'reports'
SAFE_ID = re.compile(r'^[A-Za-z0-9._-]+$')
_STATIC_SEM = threading.Semaphore(12)
_DEV_BUILD_ID = '0'
_DEV_SERVER_STARTED_AT = 0


def _compute_dev_build_id(root):
    latest = 0.0
    js_root = os.path.join(root, 'js')
    if not os.path.isdir(js_root):
        return str(int(time.time()))
    for dirpath, _, filenames in os.walk(js_root):
        for name in filenames:
            if not name.endswith(('.js', '.mjs')):
                continue
            try:
                latest = max(latest, os.path.getmtime(os.path.join(dirpath, name)))
            except OSError:
                pass
    if latest <= 0:
        return str(int(time.time()))
    return str(int(latest * 1000))


class ThreadingEcoSimServer(ThreadingHTTPServer):
    daemon_threads = True
    allow_reuse_address = True


class EcoSimHandler(SimpleHTTPRequestHandler):
    protocol_version = 'HTTP/1.1'

    def _send_dev_cache_headers(self):
        self.send_header('Cache-Control', 'no-store, no-cache, must-revalidate, max-age=0')
        self.send_header('Pragma', 'no-cache')
        self.send_header('Expires', '0')
        self.send_header('Surrogate-Control', 'no-store')
        self.send_header('CDN-Cache-Control', 'no-store')
        self.send_header('X-Dev-Build', _DEV_BUILD_ID)

    def end_headers(self):
        self._send_dev_cache_headers()
        self.send_header('Connection', 'close')
        super().end_headers()

    def do_GET(self):
        path_only = self.path.split('?', 1)[0]
        if path_only in ('/', ''):
            self.send_response(302)
            self.send_header('Location', f'/wildlands-ecosim.html?dev={_DEV_BUILD_ID}')
            self.end_headers()
            return
        if self.path.startswith('/api/batch-reports'):
            self.handle_batch_reports_get()
            return
        if not _STATIC_SEM.acquire(timeout=30):
            self.send_error(503, 'Server busy')
            return
        try:
            self._serve_static_file()
        except (BrokenPipeError, ConnectionResetError):
            pass
        except Exception as err:
            self.log_error('static GET failed: %s', err)
            if not self.wfile.closed:
                try:
                    self.send_error(500, 'Internal server error')
                except Exception:
                    pass
        finally:
            _STATIC_SEM.release()

    def _serve_static_file(self):
        path = self.translate_path(self.path.split('?', 1)[0])
        if os.path.isdir(path):
            self.send_error(404, 'File not found')
            return
        if not os.path.isfile(path):
            self.send_error(404, 'File not found')
            return

        with open(path, 'rb') as file_obj:
            data = file_obj.read()

        path_only = self.path.split('?', 1)[0]
        if path_only.endswith('.html'):
            text = data.decode('utf-8', errors='replace')
            text = text.replace('__DEV_BUILD_ID__', _DEV_BUILD_ID)
            data = text.encode('utf-8')
        elif path_only.endswith('.js') or path_only.endswith('.mjs'):
            try:
                stamp = int(os.path.getmtime(path) * 1000)
            except OSError:
                stamp = int(time.time() * 1000)
            prefix = f'/* dev:{stamp} */\n'.encode('utf-8')
            if not data.startswith(prefix):
                data = prefix + data

        self.send_response(200)
        if path_only.endswith('.js') or path_only.endswith('.mjs'):
            ctype = 'text/javascript; charset=utf-8'
        elif path_only.endswith('.json'):
            ctype = 'application/json; charset=utf-8'
        elif path_only.endswith('.html'):
            ctype = 'text/html; charset=utf-8'
        else:
            ctype = self.guess_type(path)
        self.send_header('Content-type', ctype)
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        try:
            self.wfile.write(data)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def do_POST(self):
        if self.path == '/api/batch-reports':
            self.handle_batch_reports_post()
            return
        self.send_error(404, 'Not found')

    def do_DELETE(self):
        if self.path.startswith('/api/batch-reports/'):
            self.handle_batch_reports_delete()
            return
        self.send_error(404, 'Not found')

    def handle_batch_reports_get(self):
        os.makedirs(REPORTS_DIR, exist_ok=True)
        path = self.path.split('?', 1)[0].rstrip('/')
        if path == '/api/batch-reports':
            rows = []
            for name in sorted(os.listdir(REPORTS_DIR), reverse=True):
                if not name.endswith('.json'):
                    continue
                run_id = name[:-5]
                file_path = os.path.join(REPORTS_DIR, name)
                try:
                    with open(file_path, 'r', encoding='utf-8') as f:
                        data = json.load(f)
                    rows.append({
                        'runId': data.get('runId') or data.get('campaignId') or run_id,
                        'outcome': data.get('outcome') or 'campaign',
                        'startedAt': data.get('startedAt'),
                        'finalPop': (data.get('summary') or {}).get('finalPop'),
                        'fuzzTrials': data.get('fuzzTrials'),
                    })
                except (OSError, json.JSONDecodeError):
                    rows.append({'runId': run_id, 'outcome': 'error'})
            self.send_json(200, rows)
            return

        if path.startswith('/api/batch-reports/'):
            run_id = path.rsplit('/', 1)[-1]
            if not SAFE_ID.match(run_id):
                self.send_error(400, 'Invalid run id')
                return
            file_path = os.path.join(REPORTS_DIR, f'{run_id}.json')
            if not os.path.isfile(file_path):
                self.send_error(404, 'Report not found')
                return
            try:
                with open(file_path, 'r', encoding='utf-8') as f:
                    data = json.load(f)
                self.send_json(200, data)
            except (OSError, json.JSONDecodeError) as err:
                self.send_error(500, str(err))
            return

        self.send_error(404, 'Not found')

    def handle_batch_reports_post(self):
        length = int(self.headers.get('Content-Length', 0))
        body = self.rfile.read(length) if length else b'{}'
        try:
            data = json.loads(body.decode('utf-8'))
        except json.JSONDecodeError:
            self.send_error(400, 'Invalid JSON')
            return

        run_id = data.get('runId') or data.get('campaignId')
        if not run_id or not SAFE_ID.match(str(run_id)):
            self.send_error(400, 'Missing or invalid runId/campaignId')
            return

        os.makedirs(REPORTS_DIR, exist_ok=True)
        path = os.path.join(REPORTS_DIR, f'{run_id}.json')
        try:
            with open(path, 'w', encoding='utf-8') as f:
                json.dump(data, f, indent=2)
            self.send_json(201, {'ok': True, 'runId': run_id, 'path': path})
        except OSError as err:
            self.send_error(500, str(err))

    def handle_batch_reports_delete(self):
        path = self.path.split('?', 1)[0].rstrip('/')
        run_id = path.rsplit('/', 1)[-1]
        if not SAFE_ID.match(run_id):
            self.send_error(400, 'Invalid run id')
            return
        file_path = os.path.join(REPORTS_DIR, f'{run_id}.json')
        if not os.path.isfile(file_path):
            self.send_json(200, {'ok': True, 'deleted': False, 'runId': run_id})
            return
        try:
            os.remove(file_path)
            self.send_json(200, {'ok': True, 'deleted': True, 'runId': run_id})
        except OSError as err:
            self.send_error(500, str(err))

    def send_json(self, code, payload):
        raw = json.dumps(payload).encode('utf-8')
        self.send_response(code)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(raw)))
        self.end_headers()
        self.wfile.write(raw)

    def log_message(self, format, *args):
        print(f"[serve] {self.address_string()} {format % args}")


def main():
    global _DEV_BUILD_ID, _DEV_SERVER_STARTED_AT
    port = int(os.environ.get('PORT', '8765'))
    batch_mode = os.environ.get('BATCH_SERVER', '') == '1'
    root = os.path.dirname(os.path.abspath(__file__))
    os.chdir(root)
    os.makedirs(os.path.join(root, REPORTS_DIR), exist_ok=True)
    _DEV_BUILD_ID = _compute_dev_build_id(root)
    _DEV_SERVER_STARTED_AT = int(time.time())
    server_cls = HTTPServer if batch_mode else ThreadingEcoSimServer
    server = server_cls(('127.0.0.1', port), EcoSimHandler)
    mode = 'batch (sequential)' if batch_mode else 'threaded'
    game_url = f'http://127.0.0.1:{port}/wildlands-ecosim.html?dev={_DEV_BUILD_ID}'
    print(f'EcoSim dev server ({mode}): {game_url}')
    print(f'Dev build id: {_DEV_BUILD_ID} (use serve.py — not python -m http.server)')
    print(f'Batch test runner: http://127.0.0.1:{port}/batch-test.html?dev={_DEV_BUILD_ID}')
    print('All responses sent with no-store cache headers; JS modules stamped on each save.')
    print('Press Ctrl+C to stop.')
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print('\nStopping server…')
    finally:
        server.server_close()


if __name__ == '__main__':
    main()
