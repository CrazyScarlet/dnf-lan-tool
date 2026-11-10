# -*- coding: utf-8 -*-
"""
修改服务端对外通告的 IP（Radmin 主机的 IP 变了以后用这个）

用法：
    python dnf_set_server_ip.py 你的联机 IP

做的事：
    1. SSH 到虚拟机，备份所有 cfg 到 /root/dnf-cfg-backup-时间戳.tar.gz
    2. 把 /home/neople/game/cfg/*.cfg 与 /home/neople/channel/cfg/*.cfg 里的旧 IP 全部换成新 IP
    3. 重启服务端并等待端口就绪
"""
import re
import os
import socket
import sys
import time

import paramiko

SSH_HOST = os.environ.get("DNF_SSH_HOST", "192.168.200.131")
SSH_PORT = int(os.environ.get("DNF_SSH_PORT", "22"))
SSH_USER = os.environ.get("DNF_SSH_USER", "root")
SSH_PWD = os.environ.get("DNF_SSH_PWD", "")

CFG_DIRS = "/home/neople/game/cfg /home/neople/channel/cfg"
READY_PORTS = [7001, 10011, 10056]


def port_open(host, port, timeout=1.5):
    s = socket.socket()
    s.settimeout(timeout)
    try:
        s.connect((host, port))
        return True
    except Exception:
        return False
    finally:
        s.close()


def sh(cmd, timeout=180):
    cli = paramiko.SSHClient()
    cli.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    cli.connect(SSH_HOST, SSH_PORT, SSH_USER, SSH_PWD, timeout=20, banner_timeout=30)
    try:
        stdin, stdout, stderr = cli.exec_command(cmd, timeout=timeout)
        out = stdout.read().decode("utf-8", "replace")
        err = stderr.read().decode("utf-8", "replace")
        return stdout.channel.recv_exit_status(), out, err
    finally:
        cli.close()


def main():
    if len(sys.argv) < 2:
        print("用法: python dnf_set_server_ip.py 新的IP")
        sys.exit(2)
    new_ip = sys.argv[1].strip()
    if not re.match(r"^\d{1,3}(\.\d{1,3}){3}$", new_ip):
        print(f"IP 格式不对：{new_ip}")
        sys.exit(2)

    if not port_open(SSH_HOST, SSH_PORT):
        print(f"连不上虚拟机 {SSH_HOST}:22 —— 请先在工具里点【一键启动联机】")
        sys.exit(1)

    rc, out, _ = sh("grep -h -E '^[[:space:]]*ip[[:space:]]*=' /home/neople/game/cfg/siroco11.cfg | head -1")
    m = re.search(r"ip\s*=\s*([0-9.]+)", out)
    if not m:
        print("读不到当前 IP，请检查 siroco11.cfg")
        sys.exit(1)
    old_ip = m.group(1)
    print(f"当前服务端 IP：{old_ip}")
    print(f"目标 IP：      {new_ip}")
    if old_ip == new_ip:
        print("IP 没有变化，不需要修改。")
        sys.exit(0)

    ts = time.strftime("%Y%m%d-%H%M%S")
    backup = f"/root/dnf-cfg-backup-{ts}.tar.gz"
    rc, _, err = sh(f"tar czf {backup} {CFG_DIRS}")
    if rc != 0:
        print(f"备份失败：{err}")
        sys.exit(1)
    print(f"√ 已备份：{backup}")

    rc, files, _ = sh(f"grep -rl '{old_ip}' {CFG_DIRS} | tr '\\n' ' '")
    files = files.strip()
    if not files:
        print("没有文件包含旧 IP，直接重启服务端。")
    else:
        n = len(files.split())
        print(f"共 {n} 个配置文件需要修改……")
        rc, _, err = sh(f"grep -rl '{old_ip}' {CFG_DIRS} | xargs sed -i 's/{old_ip}/{new_ip}/g'")
        if rc != 0:
            print(f"修改失败：{err}")
            sys.exit(1)

    rc, left, _ = sh(f"grep -rl '{old_ip}' {CFG_DIRS} | head -5")
    if left.strip():
        print(f"仍有文件残留旧 IP：{left}")
        sys.exit(1)
    print("√ 配置已全部更新")

    print("正在重启服务端……")
    sh("bash /root/stop > /dev/null 2>&1; sleep 2; "
       "cd /root && setsid bash /root/run > /root/run-last.log 2>&1 < /dev/null &", timeout=60)

    t0 = time.time()
    while time.time() - t0 < 150:
        if all(port_open(SSH_HOST, p) for p in READY_PORTS):
            print(f"√ 服务端已重启并就绪（用时 {int(time.time() - t0)} 秒）")
            print("\n别忘了同时把客户端配置也改掉：")
            print(f"    python dnf_set_ip.py {new_ip}")
            print(f"    以及 hosts 里的 start.dnf.tw 指向 {new_ip}")
            return
        time.sleep(3)
    print("× 等待端口就绪超时，请点工具里的【一键检查状态】，或看 /root/run-last.log")


if __name__ == "__main__":
    main()
