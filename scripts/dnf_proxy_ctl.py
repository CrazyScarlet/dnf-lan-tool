# -*- coding: utf-8 -*-
"""
端口转发器控制：启动 / 停止 / 状态

用法：
    python dnf_proxy_ctl.py start
    python dnf_proxy_ctl.py stop
    python dnf_proxy_ctl.py status
"""
import os
import socket
import subprocess
import sys
import time

BASE = os.path.dirname(os.path.abspath(__file__))
PROXY = os.path.join(BASE, "dnf_proxy.py")
_BUNDLED = os.path.join(os.path.dirname(os.path.abspath(__file__)), "python", "pythonw.exe")
PYTHONW = _BUNDLED if os.path.isfile(_BUNDLED) else r"D:\Python\pythonw.exe"
WATCH_PORT = 7101


def port_open(port, host="127.0.0.1", timeout=1.5):
    s = socket.socket()
    s.settimeout(timeout)
    try:
        s.connect((host, port))
        return True
    except Exception:
        return False
    finally:
        s.close()


def listener_pid(port):
    """从 netstat 里找出监听某端口的进程 PID（wmic 在新版 Windows 已移除，不能用）。"""
    try:
        out = subprocess.run(["netstat", "-ano"], capture_output=True, text=True,
                             errors="ignore", timeout=25).stdout
    except Exception:
        return None
    for line in out.splitlines():
        parts = line.split()
        if len(parts) >= 5 and parts[0].upper() == "TCP" and parts[3].upper() == "LISTENING":
            if parts[1].rsplit(":", 1)[-1] == str(port):
                try:
                    return int(parts[4])
                except ValueError:
                    return None
    return None


def proc_name(pid):
    try:
        out = subprocess.run(["tasklist", "/fi", f"PID eq {pid}", "/fo", "csv", "/nh"],
                             capture_output=True, text=True, errors="ignore",
                             timeout=25).stdout.strip()
    except Exception:
        return ""
    if not out.startswith('"'):
        return ""
    return out.strip('"').split('","')[0].strip('"').lower()


def proxy_pid():
    pid = listener_pid(WATCH_PORT)
    if pid and proc_name(pid).startswith("python"):
        return pid
    return None


def start():
    pid = proxy_pid()
    if pid:
        print(f"端口转发器已经在运行（进程 {pid}），无需重复启动。")
        return 0
    if port_open(WATCH_PORT) and pid is None:
        print(f"注意：{WATCH_PORT} 端口已被其它程序占用（进程 {listener_pid(WATCH_PORT)}），转发器可能启动失败。")
    if not os.path.isfile(PYTHONW):
        print(f"找不到 {PYTHONW}，请确认 Python 安装路径。")
        return 2
    subprocess.Popen([PYTHONW, PROXY], cwd=BASE,
                     creationflags=0x00000008, close_fds=True)
    for _ in range(24):
        time.sleep(0.5)
        if proxy_pid():
            print(f"√ 端口转发器已启动（进程 {proxy_pid()}，后台运行，日志见 proxy.log）")
            return 0
    print("× 启动后 %s 仍无响应，请查看 proxy.log" % WATCH_PORT)
    return 1


def stop():
    pid = proxy_pid()
    if not pid:
        print("没有发现正在运行的端口转发器。")
        return 0
    r = subprocess.run(["taskkill", "/F", "/PID", str(pid)],
                       capture_output=True, text=True, errors="ignore")
    if r.returncode == 0:
        print(f"√ 已结束端口转发器进程 {pid}")
    else:
        print(f"× 结束进程失败：{(r.stdout or '') + (r.stderr or '')}")
        return 1
    return 0


def status():
    pid = proxy_pid()
    print(f"转发器进程: {pid if pid else '未运行'}")
    for port in (7101, 10011, 10186, 7200, 3306, 30803, 30603):
        print(f"  127.0.0.1:{port} {'监听中' if port_open(port) else '未监听'}")
    return 0


if __name__ == "__main__":
    action = sys.argv[1].lower() if len(sys.argv) > 1 else "status"
    sys.exit({"start": start, "stop": stop, "status": status}.get(action, status)())
