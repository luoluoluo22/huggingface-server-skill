import os
import sys
import ssl
import socket
import platform
from datetime import datetime

import requests


def p(msg: str) -> None:
    print(msg, flush=True)


def mask_token(token: str) -> str:
    if not token:
        return "(missing)"
    if len(token) <= 8:
        return "*" * len(token)
    return token[:4] + "*" * (len(token) - 8) + token[-4:]


def check_dns(host: str) -> None:
    p(f"\n[DNS] {host}")
    try:
        infos = socket.getaddrinfo(host, 443, type=socket.SOCK_STREAM)
        addrs = sorted({item[4][0] for item in infos})
        p(f"OK - resolved {len(addrs)} address(es): {', '.join(addrs)}")
    except Exception as e:
        p(f"FAIL - {type(e).__name__}: {e}")


def check_tcp(host: str, port: int = 443, timeout: int = 10) -> None:
    p(f"\n[TCP] {host}:{port}")
    try:
        with socket.create_connection((host, port), timeout=timeout):
            p("OK - tcp connect success")
    except Exception as e:
        p(f"FAIL - {type(e).__name__}: {e}")


def check_tls(host: str, port: int = 443, timeout: int = 10) -> None:
    p(f"\n[TLS] {host}:{port}")
    try:
        ctx = ssl.create_default_context()
        with socket.create_connection((host, port), timeout=timeout) as sock:
            with ctx.wrap_socket(sock, server_hostname=host) as ssock:
                p(f"OK - TLS {ssock.version()} cipher={ssock.cipher()[0]}")
    except Exception as e:
        p(f"FAIL - {type(e).__name__}: {e}")


def req(method: str, url: str, timeout: int = 15, headers=None, trust_env: bool = True) -> None:
    p(f"\n[HTTP] {method} {url} (trust_env={trust_env})")
    s = requests.Session()
    s.trust_env = trust_env
    try:
        r = s.request(method, url, timeout=timeout, headers=headers)
        text_preview = (r.text or "").replace("\n", " ")[:180]
        p(f"OK - status={r.status_code}, len={len(r.text or '')}, preview={text_preview}")
    except Exception as e:
        p(f"FAIL - {type(e).__name__}: {e}")


def main() -> int:
    p("=== HF Network Diagnostic ===")
    p(f"time: {datetime.now().isoformat(timespec='seconds')}")
    p(f"python: {sys.version.split()[0]}")
    p(f"os: {platform.platform()}")

    token = os.environ.get("HF_TOKEN", "")
    p(f"HF_TOKEN: {mask_token(token)}")

    p("\n[ENV_PROXY]")
    for k in ["HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "http_proxy", "https_proxy", "all_proxy", "no_proxy"]:
        v = os.environ.get(k)
        p(f"{k}={v if v else ''}")

    host = "huggingface.co"
    check_dns(host)
    check_tcp(host, 443)
    check_tls(host, 443)

    req("GET", "https://huggingface.co", trust_env=True)
    req("GET", "https://huggingface.co", trust_env=False)
    req("GET", "https://huggingface.co/api/whoami-v2", trust_env=True)

    if token:
        headers = {"Authorization": f"Bearer {token}"}
        req("GET", "https://huggingface.co/api/whoami-v2", headers=headers, trust_env=True)
        req("GET", "https://huggingface.co/api/whoami-v2", headers=headers, trust_env=False)

    p("\n=== END ===")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
