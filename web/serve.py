#!/usr/bin/env python3
"""Serveur statique minimal avec les en-têtes COOP/COEP exigés par SharedArrayBuffer.
Usage : python3 web/serve.py   puis ouvrir http://localhost:8000/index-mt.html
"""
import http.server, socketserver, os

PORT = 8000
WEB_DIR = os.path.dirname(os.path.abspath(__file__))

class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *a, **k):
        super().__init__(*a, directory=WEB_DIR, **k)
    def end_headers(self):
        self.send_header("Cross-Origin-Opener-Policy", "same-origin")
        self.send_header("Cross-Origin-Embedder-Policy", "require-corp")
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

if __name__ == "__main__":
    with socketserver.ThreadingTCPServer(("127.0.0.1", PORT), Handler) as httpd:
        print(f"Sert {WEB_DIR} sur http://localhost:{PORT}")
        print(f"  mono-thread : http://localhost:{PORT}/index.html")
        print(f"  multi-thread: http://localhost:{PORT}/index-mt.html")
        httpd.serve_forever()
