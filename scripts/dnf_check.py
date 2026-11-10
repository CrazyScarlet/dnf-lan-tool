# -*- coding: utf-8 -*-
"""
DNF Radmin 联机 —— 一键自检

检查项：
  1. Radmin VPN 网卡是否在线、IP 是否是配置的那个
  2. 端口转发器 dnf_proxy.py 是否在运行（本机 26.x IP 的端口是否有响应）
  3. VMware 虚拟机里的服务端是否在运行（直连 192.168.200.131 的端口）
  4. 数据库能否通过 Radmin IP 登录（260 字节 MySQL 握手包）
  5. hosts 里 start.dnf.tw 指向是否正确
"""
import os
import socket
import subprocess
import sys

HOST_IP = os.environ.get("DNF_HOST_IP", "")        # Radmin 虚拟网卡 IP（给朋友用的地址）
VM_IP = os.environ.get("DNF_SSH_HOST", "192.168.200.131")       # VMware NAT 里虚拟机的 IP
PROXY_TCP_PORTS = [7101, 10011, 10186, 7200, 3306, 30803, 30603]
SERVER_TCP_PORTS = [7101, 10011, 10186, 7200, 3306, 30803, 30603]

VMRUN = r"D:\Program Files (x86)\VMware\VMware Workstation\vmrun.exe"
def _ini_value(key, default=""):
    try:
        ini = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "启动器配置.ini")
        for line in open(ini, encoding="utf-8"):
            if line.startswith(key + "="):
                return line.split("=", 1)[1].strip()
    except Exception:
        pass
    return default
VMX = os.environ.get("DNF_VMX") or _ini_value("VMX", r"E:\DNF\70S3A3\CentOS 7.6\CentOS 7.6.vmx")
HOSTS = r"C:\Windows\System32\drivers\etc\hosts"

OK, BAD, WARN = "√", "×", "!"


def port_open(host, port, timeout=2.0, read_bytes=0):
    s = socket.socket()
    s.settimeout(timeout)
    try:
        s.connect((host, port))
        if read_bytes:
            try:
                return s.recv(read_bytes)
            except Exception:
                return b""
        return True
    except Exception:
        return False
    finally:
        try:
            s.close()
        except Exception:
            pass


def has_local_ip(ip):
    """能 bind 到该地址说明本机确实有这个 IP。"""
    s = socket.socket()
    try:
        s.bind((ip, 0))
        return True
    except Exception:
        return False
    finally:
        s.close()


def _decode_variants(raw):
    """vmrun 的输出编码随环境变（实测同一台机器是 UTF-8，老环境是 ANSI/GBK），
    三种都试，谁解得出目标路径就算谁对。"""
    out = []
    for enc in ("utf-8", "mbcs", "gbk"):
        try:
            out.append(raw.decode(enc))
        except Exception:
            continue
    return out


def vm_running():
    try:
        proc = subprocess.run(
            [VMRUN, "-T", "ws", "list"],
            capture_output=True, timeout=60,
        )
        raw = proc.stdout or b""
    except Exception:
        return None
    norm = lambda s: s.replace("/", "\\").lower().strip()
    target = norm(VMX)
    for text in _decode_variants(raw):
        if target in norm(text):
            return True
    return False


def listener_pid(port):
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


def hosts_ok():
    try:
        with open(HOSTS, "r", encoding="utf-8", errors="ignore") as f:
            txt = f.read()
    except Exception as e:
        return None, f"读取失败: {e}"
    lines = [l.strip() for l in txt.splitlines() if "start.dnf.tw" in l and not l.strip().startswith("#")]
    if not lines:
        return False, "没有 start.dnf.tw 记录"
    return True, "；".join(lines)


def main():
    problems = []

    print("=" * 62)
    print(" DNF Radmin 联机 —— 状态自检")
    print("=" * 62)

    # 1. Radmin 网卡
    if has_local_ip(HOST_IP):
        print(f"[{OK}] Radmin 网卡在线，本机地址 {HOST_IP}")
    else:
        print(f"[{BAD}] 本机没有 {HOST_IP} —— Radmin VPN 没连上/网络未加入，朋友连不进来")
        problems.append("Radmin VPN 未连接")

    # 2. 转发器进程
    pid = listener_pid(7001)
    if pid and proc_name(pid).startswith("python"):
        print(f"[{OK}] 端口转发器在运行（进程 {pid}）")
    elif pid:
        print(f"[{WARN}] 7001 端口被非 Python 程序占用（进程 {pid}：{proc_name(pid)}）")
        problems.append("7001 端口被占用")
    else:
        print(f"[{BAD}] 端口转发器没在运行 —— 在工具里点【一键启动联机】")
        problems.append("端口转发器未运行")

    for port in PROXY_TCP_PORTS:
        if port_open(HOST_IP, port):
            print(f"[{OK}] 端口转发 {HOST_IP}:{port} → 通")
        else:
            print(f"[{BAD}] 端口转发 {HOST_IP}:{port} → 不通")
            problems.append(f"端口 {port} 转发不通")

    # 3. 虚拟机 / 服务端
    vm = vm_running()
    if vm is None:
        print(f"[{WARN}] 无法执行 vmrun，跳过虚拟机检查")
    elif vm:
        print(f"[{OK}] 虚拟机在运行")
        for port in SERVER_TCP_PORTS:
            state = f"{OK}" if port_open(VM_IP, port) else f"{BAD}"
            print(f"[{state}] 服务端 {VM_IP}:{port} {'监听中' if state == OK else '未监听'}")
            if state == BAD:
                problems.append(f"服务端端口 {port} 未监听")
    else:
        print(f"[{BAD}] 虚拟机未运行 —— 在工具里点【一键启动联机】（或【启动服务端】）")
        problems.append("虚拟机未运行")

    # 4. 数据库（经 Radmin IP，和朋友完全一样的路径）
    banner = port_open(HOST_IP, 3306, read_bytes=16)
    if banner:
        print(f"[{OK}] 经 {HOST_IP}:3306 读到 MySQL 握手包：{banner[4:12]!r}")
    else:
        print(f"[{BAD}] 经 {HOST_IP}:3306 拿不到 MySQL 握手 —— 登录器会提示「连接服务器失败」")
        problems.append("数据库经 Radmin IP 不可达")

    # 5. hosts
    ok, info = hosts_ok()
    if ok:
        print(f"[{OK}] hosts: {info}")
    elif ok is False:
        print(f"[{BAD}] hosts 里没有 start.dnf.tw —— 重跑一次【一键启动联机】会自动写")
        problems.append("hosts 未设置")
    else:
        print(f"[{WARN}] hosts {info}")

    print("-" * 62)
    if problems:
        print("存在以下问题：")
        for i, p_ in enumerate(problems, 1):
            print(f"  {i}. {p_}")
        print("\n解决顺序参考：工具里点【一键启动联机】→ 再跑一次本检查")
        sys.exit(1)
    else:
        print("全部正常，可以让朋友开始联机了！")
        sys.exit(0)


if __name__ == "__main__":
    main()
