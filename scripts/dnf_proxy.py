# -*- coding: utf-8 -*-
"""
DNF Radmin 联机端口转发器
把本机（Radmin 虚拟网卡 26.x.x.x）上收到的连接，转发到 VMware NAT 虚拟机里的 DNF 服务端 192.168.200.131。

用法: pythonw dnf_proxy.py   （无窗口运行；日志写在同目录 proxy.log）
"""
import asyncio
import logging
import os
import sys
import time

LISTEN_HOST = "0.0.0.0"          # 监听所有网卡（含 Radmin VPN 虚拟网卡）
UPSTREAM_HOST = "192.168.200.131"  # VMware 虚拟机里的服务端
TCP_PORTS = [7101, 7001, 10011, 10056, 10186, 7200, 3306, 30803, 30603]
UDP_PORTS = [7101, 7001, 11011, 11056, 11186, 2311, 2312, 2313, 24411, 24456, 8200]
UDP_IDLE = 120  # 秒，UDP 会话空闲多久后回收

BASE = os.path.dirname(os.path.abspath(__file__))
LOG_PATH = os.path.join(BASE, "proxy.log")

log = logging.getLogger("dnfproxy")
log.setLevel(logging.INFO)
_fmt = logging.Formatter("%(asctime)s %(levelname)s %(message)s")
_fh = logging.FileHandler(LOG_PATH, encoding="utf-8")
_fh.setFormatter(_fmt)
log.addHandler(_fh)


def p(*a):
    msg = " ".join(str(x) for x in a)
    log.info(msg)
    try:
        print(msg, flush=True)
    except Exception:
        pass


async def _pump(reader, writer, tag):
    try:
        while True:
            data = await reader.read(65536)
            if not data:
                break
            writer.write(data)
            await writer.drain()
    except Exception:
        pass
    finally:
        try:
            if writer.can_write_eof():
                writer.write_eof()
        except Exception:
            pass
        try:
            writer.close()
        except Exception:
            pass


async def handle_tcp(client_r, client_w, port):
    peer = client_w.get_extra_info("peername")
    try:
        up_r, up_w = await asyncio.open_connection(UPSTREAM_HOST, port)
    except Exception as e:
        p(f"[TCP:{port}] 连接服务端失败 {e} (来自 {peer})")
        client_w.close()
        return
    p(f"[TCP:{port}] 新连接 {peer}")
    await asyncio.gather(
        _pump(client_r, up_w, f"c->s:{port}"),
        _pump(up_r, client_w, f"s->c:{port}"),
    )
    p(f"[TCP:{port}] 连接关闭 {peer}")


class _UpstreamUdp(asyncio.DatagramProtocol):
    """每个客户端会话对应一个上游 UDP 套接字。"""

    def __init__(self, proxy, client_addr):
        self.proxy = proxy
        self.client_addr = client_addr
        self.transport = None

    def connection_made(self, transport):
        self.transport = transport

    def datagram_received(self, data, addr):
        try:
            self.proxy.transport.sendto(data, self.client_addr)
            self.proxy.touch(self.client_addr)
        except Exception:
            pass

    def error_received(self, exc):
        pass


class UdpProxy(asyncio.DatagramProtocol):
    def __init__(self, port):
        self.port = port
        self.transport = None
        self.sessions = {}  # client_addr -> [upstream_transport, last_seen]

    def connection_made(self, transport):
        self.transport = transport

    def touch(self, addr):
        s = self.sessions.get(addr)
        if s:
            s[1] = time.monotonic()

    def datagram_received(self, data, addr):
        s = self.sessions.get(addr)
        if s is None:
            task = asyncio.create_task(self._new_session(addr, data))
            task.add_done_callback(lambda t: t.exception() if not t.cancelled() else None)
            return
        s[1] = time.monotonic()
        try:
            s[0].sendto(data)
        except Exception as e:
            p(f"[UDP:{self.port}] 转发失败 {e}")

    async def _new_session(self, addr, data):
        loop = asyncio.get_running_loop()
        try:
            transport, proto = await loop.create_datagram_endpoint(
                lambda: _UpstreamUdp(self, addr),
                remote_addr=(UPSTREAM_HOST, self.port),
            )
        except Exception as e:
            p(f"[UDP:{self.port}] 建立会话失败 {e}")
            return
        self.sessions[addr] = [transport, time.monotonic()]
        p(f"[UDP:{self.port}] 新会话 {addr}")
        try:
            transport.sendto(data)
        except Exception as e:
            p(f"[UDP:{self.port}] 首发失败 {e}")

    def sweep(self):
        now = time.monotonic()
        for addr in list(self.sessions):
            tr, ts = self.sessions[addr]
            if now - ts > UDP_IDLE:
                try:
                    tr.close()
                except Exception:
                    pass
                del self.sessions[addr]


async def sweeper(proxies):
    while True:
        await asyncio.sleep(30)
        for pr in proxies:
            pr.sweep()


async def main():
    loop = asyncio.get_running_loop()
    proxies = []
    tasks = []

    for port in TCP_PORTS:
        try:
            srv = await asyncio.start_server(
                lambda r, w, p_=port: handle_tcp(r, w, p_),
                host=LISTEN_HOST, port=port,
            )
            p(f"[TCP] 监听 {LISTEN_HOST}:{port} -> {UPSTREAM_HOST}:{port}")
        except Exception as e:
            p(f"[TCP] 监听 {port} 失败: {e}")
            continue
        tasks.append(srv)

    for port in UDP_PORTS:
        try:
            transport, proto = await loop.create_datagram_endpoint(
                lambda p_=port: UdpProxy(p_),
                local_addr=(LISTEN_HOST, port),
                reuse_port=False,
            )
            p(f"[UDP] 监听 {LISTEN_HOST}:{port} -> {UPSTREAM_HOST}:{port}")
            proxies.append(proto)
        except Exception as e:
            p(f"[UDP] 监听 {port} 失败: {e}")

    if proxies:
        tasks.append(asyncio.create_task(sweeper(proxies)))

    p("转发器已启动，等待连接……")
    await asyncio.gather(*[t.serve_forever() if isinstance(t, asyncio.AbstractServer) else t for t in tasks])


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        p("已退出")
