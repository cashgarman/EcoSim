#!/usr/bin/env python3
"""Threaded static file server for EcoSim ES module development."""

from http.server import ThreadingHTTPServer, SimpleHTTPRequestHandler
import os


class EcoSimHandler(SimpleHTTPRequestHandler):
    def end_headers(self):
        self.send_header('Cache-Control', 'no-store, no-cache, must-revalidate')
        self.send_header('Pragma', 'no-cache')
        super().end_headers()

    def send_head(self):
        path = self.translate_path(self.path)
        if os.path.isdir(path):
            return super().send_head()

        ctype = self.guess_type(path)
        try:
            file_obj = open(path, 'rb')
        except OSError:
            self.send_error(404, 'File not found')
            return None

        try:
            fs = os.fstat(file_obj.fileno())
            self.send_response(200)
            self.send_header('Content-type', ctype)
            self.send_header('Content-Length', str(fs.st_size))
            self.send_header('Last-Modified', self.date_time_string(fs.st_mtime))
            self.end_headers()
            return file_obj
        except Exception:
            file_obj.close()
            raise

    def log_message(self, format, *args):
        print(f"[serve] {self.address_string()} {format % args}")


def main():
    port = int(os.environ.get('PORT', '8765'))
    os.chdir(os.path.dirname(os.path.abspath(__file__)))
    server = ThreadingHTTPServer(('127.0.0.1', port), EcoSimHandler)
    print(f'EcoSim dev server: http://127.0.0.1:{port}/wildlands-ecosim.html')
    print('Press Ctrl+C to stop.')
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print('\nStopping server…')
    finally:
        server.server_close()


if __name__ == '__main__':
    main()
