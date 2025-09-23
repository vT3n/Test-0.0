# listener.py
from http.server import HTTPServer, BaseHTTPRequestHandler

class Handler(BaseHTTPRequestHandler):
    def do_POST(self):
        n = int(self.headers.get('Content-Length', '0'))
        data = self.rfile.read(n).decode('utf-8', errors='replace')
        print(data, flush=True)
        self.send_response(200)
        self.end_headers()

    def log_message(self, fmt, *args):
        # quiet default access log
        pass

if __name__ == '__main__':
    srv = HTTPServer(('127.0.0.1', 5055), Handler)
    print('Listening on http://127.0.0.1:5055/ingest/')
    srv.serve_forever()
