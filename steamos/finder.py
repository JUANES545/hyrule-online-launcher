"""Server finder of the launcher's Join mode (mirrors src/Launcher/ServerFinder.cs): looks for Hyrule Online servers on the Tailscale peers and on the
local network with a UDP beacon (port 5051): the request `HYRULE?` is answered by a hosting launcher with `HYRULE1 <game tcp port>`.
It never opens a TCP connection to the game port: the dedicated server crashes on one without a valid handshake. Python standard library only."""
import ipaddress
import json
import re
import shutil
import socket
import subprocess
import threading
import time
from collections import namedtuple
from concurrent.futures import ThreadPoolExecutor

DEFAULT_PORT = 5050
BEACON_PORT = 5051
REQUEST = b"HYRULE?"
PROBE_S, TEST_S, CLI_S = 0.4, 1.5, 3
WORKERS, MAX_HOSTS, MAX_ROWS = 64, 1024, 6
PRIVATE = [ipaddress.ip_network(n) for n in ("10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16")]
VPN_RANGE = ipaddress.ip_network("100.64.0.0/10")

Found = namedtuple("Found", "host port via ms")


def ip_of(text):
    try:
        return ipaddress.IPv4Address(text.strip())
    except ValueError:
        return None


def classify(host):
    """`vpn` inside 100.64.0.0/10 (Tailscale), `lan` for everything else."""
    ip = ip_of(host)
    return "vpn" if ip and ip in VPN_RANGE else "lan"


def parse_interfaces(text):
    """`ip -4 -o addr show` output -> [(address, prefix length)]."""
    return [(a, int(p)) for a, p in re.findall(r"inet (\d+\.\d+\.\d+\.\d+)/(\d+)", text)]


def local_interfaces():
    try:
        out = subprocess.run(["ip", "-4", "-o", "addr", "show"], capture_output=True, text=True, timeout=CLI_S, stdin=subprocess.DEVNULL).stdout
    except (OSError, subprocess.SubprocessError):
        return []
    return parse_interfaces(out)


def local_hosts(interfaces):
    """The other hosts of the /24 (or the smaller network) around each private address of this device; never the VPN range, never more than a /24."""
    own = {a for a, _ in interfaces}
    out = []
    for address, prefix in interfaces:
        ip = ip_of(address)
        if ip is None or not any(ip in n for n in PRIVATE):
            continue
        net = ipaddress.ip_interface("%s/%d" % (address, min(32, max(24, prefix)))).network
        out += [str(h) for h in net.hosts() if str(h) not in own]
    return out


def parse_tailscale(text):
    """`tailscale status --json` -> the IPv4 addresses of the online peers (never this device)."""
    try:
        peers = json.loads(text).get("Peer") or {}
        found = [a for p in peers.values() if isinstance(p, dict) and p.get("Online") is True for a in p.get("TailscaleIPs") or [] if ip_of(str(a))]
    except (ValueError, AttributeError, TypeError):
        return []
    return found


def tailscale_status():
    """The JSON of `tailscale status --json`; "" when the CLI is missing or fails (a stock Steam Deck or ROG Ally has none)."""
    exe = shutil.which("tailscale")
    if not exe:
        return ""
    try:
        return subprocess.run([exe, "status", "--json"], capture_output=True, text=True, timeout=CLI_S, stdin=subprocess.DEVNULL).stdout
    except (OSError, subprocess.SubprocessError):
        return ""


def candidates(vpn_hosts, interfaces, last=None):
    """[(host, port)] to probe: the VPN ones first, then the last used address (only an IPv4 literal, so no DNS lookup can stall a probe), then the local network;
    own addresses out, no repeats, at most MAX_HOSTS."""
    own = {a for a, _ in interfaces}
    if last and ip_of(last[0]) is None:
        last = None
    out, seen = [], set()
    for host, port in [(h, DEFAULT_PORT) for h in vpn_hosts] + ([last] if last else []) + [(h, DEFAULT_PORT) for h in local_hosts(interfaces)]:
        if host not in own and (host, port) not in seen:
            seen.add((host, port))
            out.append((host, port))
    out.sort(key=lambda c: classify(c[0]) != "vpn")  # stable: the VPN ones stay on top when the list is cut
    return out[:MAX_HOSTS]


def ping(host, timeout=PROBE_S, beacon_port=None):
    """(milliseconds >= 1, game TCP port) of a server answering the UDP beacon, or (-1, 0). Only UDP: nothing ever connects to the game port,
    because the dedicated server crashes on a TCP connection that does not send a valid handshake."""
    port = beacon_port or BEACON_PORT
    start = time.perf_counter()
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as s:
            s.sendto(REQUEST, (host, port))
            while True:
                left = start + timeout - time.perf_counter()
                if left <= 0:
                    return -1, 0
                s.settimeout(left)
                data, addr = s.recvfrom(64)
                game = parse_reply(data)
                if game and addr[1] == port:
                    return max(1, round((time.perf_counter() - start) * 1000)), game
    except (OSError, ValueError):
        return -1, 0


def parse_reply(data):
    """b"HYRULE1 <port>" -> the TCP port (1..65535), else None."""
    m = re.fullmatch(rb"HYRULE1 ([0-9]{1,5})", data)
    return int(m.group(1)) if m and 1 <= int(m.group(1)) <= 65535 else None


def answer(data, tcp_port):
    """What the responder sends back: only the exact request gets an answer."""
    return b"HYRULE1 %d" % tcp_port if data == REQUEST else None


class Beacon:
    """The responder of a hosting launcher: answers `HYRULE?` on UDP 5051 with the game's TCP port. Never raises; stays off when the port is busy."""

    def __init__(self, tcp_port, beacon_port=None):
        self.tcp_port, self.beacon_port, self.port, self.sock, self.thread, self.stopping = tcp_port, BEACON_PORT if beacon_port is None else beacon_port, 0, None, None, threading.Event()

    def start(self):
        try:
            sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            try:
                sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
                sock.bind(("0.0.0.0", self.beacon_port))
                sock.settimeout(0.25)
            except OSError:
                sock.close()
                return False
        except OSError:
            return False
        self.sock, self.port = sock, sock.getsockname()[1]
        self.thread = threading.Thread(target=self.serve, daemon=True)
        self.thread.start()
        return True

    def serve(self):
        while not self.stopping.is_set():
            try:
                data, addr = self.sock.recvfrom(64)
                reply = answer(data, self.tcp_port)
                if reply:
                    self.sock.sendto(reply, addr)
            except OSError:
                pass  # a timeout, or an unreachable sender: keep listening

    def stop(self):
        self.stopping.set()
        if self.thread:
            self.thread.join(1)
        if self.sock:
            self.sock.close()
        self.sock = self.thread = None


def order(rows):
    """VPN first, then the fastest, then the host as a number."""
    return sorted(rows, key=lambda r: (r.via != "vpn", r.ms, tuple(ip_of(r.host).packed) if ip_of(r.host) else (255,) * 4, r.host))


def autofill(current, finder_value, rows):
    """host:port to put in the address field after a search: only when it is empty or still holds what the finder put there."""
    if rows and (not current or current == finder_value):
        return "%s:%d" % (rows[0].host, rows[0].port)
    return None


def scan(cands, cancel=None, ping_fn=ping):
    """Pings each distinct candidate host once; the port of a row is the one the server's reply names."""
    hosts = list(dict.fromkeys(c[0] for c in cands))

    def one(host):
        if cancel is not None and cancel.is_set():
            return None
        ms, port = ping_fn(host, PROBE_S)
        return Found(host, port, classify(host), ms) if ms > 0 else None
    with ThreadPoolExecutor(WORKERS) as pool:
        return order([r for r in pool.map(one, hosts) if r])


def search(last=None, cancel=None, interfaces=None, status=tailscale_status, ping_fn=ping):
    """(rows, number of VPN candidates). `last` is the (host, port) last used."""
    vpn = parse_tailscale(status())
    cands = candidates(vpn, local_interfaces() if interfaces is None else interfaces, last)
    return scan(cands, cancel, ping_fn), len(vpn)


def check(host, ping_fn=ping):
    """The Test button: milliseconds the host takes to answer the beacon (1.5 s at most), or None."""
    ms, _ = ping_fn(host, TEST_S)
    return ms if ms > 0 else None
