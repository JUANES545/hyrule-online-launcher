"""Server finder of the launcher's Join mode (mirrors src/Launcher/ServerFinder.cs): looks for Hyrule Online servers on the Tailscale peers and on the
local network, by connecting to the game port and closing the connection at once. Nothing is sent. Python standard library only."""
import ipaddress
import json
import re
import shutil
import socket
import subprocess
import time
from collections import namedtuple
from concurrent.futures import ThreadPoolExecutor

DEFAULT_PORT = 5050
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


def probe(host, port, timeout=PROBE_S):
    """Milliseconds to connect (at least 1), or None."""
    start = time.perf_counter()
    try:
        socket.create_connection((host, port), timeout).close()
    except (OSError, ValueError):
        return None
    return max(1, round((time.perf_counter() - start) * 1000))


def order(rows):
    """VPN first, then the fastest, then the host as a number."""
    return sorted(rows, key=lambda r: (r.via != "vpn", r.ms, tuple(ip_of(r.host).packed) if ip_of(r.host) else (255,) * 4, r.host))


def autofill(current, finder_value, rows):
    """host:port to put in the address field after a search: only when it is empty or still holds what the finder put there."""
    if rows and (not current or current == finder_value):
        return "%s:%d" % (rows[0].host, rows[0].port)
    return None


def scan(cands, cancel=None, probe_fn=probe):
    def one(c):
        if cancel is not None and cancel.is_set():
            return None
        ms = probe_fn(c[0], c[1], PROBE_S)
        return Found(c[0], c[1], classify(c[0]), ms) if ms else None
    with ThreadPoolExecutor(WORKERS) as pool:
        return order([r for r in pool.map(one, cands) if r])


def search(last=None, cancel=None, interfaces=None, status=tailscale_status, probe_fn=probe):
    """(rows, number of VPN candidates). `last` is the (host, port) last used."""
    vpn = parse_tailscale(status())
    cands = candidates(vpn, local_interfaces() if interfaces is None else interfaces, last)
    return scan(cands, cancel, probe_fn), len(vpn)


def check(host, port, probe_fn=probe):
    """The Test button: milliseconds to connect with the longer timeout, or None."""
    return probe_fn(host, port, TEST_S)
