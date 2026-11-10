# -*- coding: utf-8 -*-
"""
把客户端配置里的服务器 IP 改成指定地址（默认 你的联机 IP）。

用法：
    python dnf_set_ip.py                  # 用默认 IP 改默认客户端目录
    python dnf_set_ip.py 你的联机 IP
    python dnf_set_ip.py 你的联机 IP "D:\\DNF\\客户端"

会修改：
    DLConfig.ini   [数据库信息] DBHost   [登录器信息] GameHost
    muqing.ini     [登录配置] 服务器IP
原文件会备份到 备份\\时间戳\\ 下。
"""
import os
import re
import shutil
import sys
import time

DEFAULT_IP = os.environ.get("DNF_HOST_IP", "")
DEFAULT_CLIENT = os.environ.get("DNF_CLIENT_DIR", r"D:\DNF\客户端")


def load(path):
    with open(path, "rb") as f:
        raw = f.read()
    for enc in ("gbk", "utf-8", "utf-8-sig"):
        try:
            return raw.decode(enc), enc
        except UnicodeDecodeError:
            continue
    return raw.decode("latin-1"), "latin-1"


def save(path, text, enc):
    with open(path, "wb") as f:
        f.write(text.encode(enc, "replace"))


def patch(path, rules, backup_dir):
    if not os.path.isfile(path):
        print(f"  - 跳过（不存在）：{path}")
        return 0
    os.makedirs(backup_dir, exist_ok=True)
    shutil.copy2(path, os.path.join(backup_dir, os.path.basename(path)))
    text, enc = load(path)
    total = 0
    for pattern, repl in rules:
        text, n = re.subn(pattern, repl, text, flags=re.MULTILINE)
        total += n
        print(f"  - {os.path.basename(path)}: {pattern} 替换 {n} 处")
    if total:
        save(path, text, enc)
    return total


def _ini_client(default):
    """读工具根目录 启动器配置.ini 的 CLIENTDIR；读不到用 default。"""
    try:
        ini = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "启动器配置.ini")
        for line in open(ini, encoding="utf-8"):
            if line.startswith("CLIENTDIR="):
                v = line.split("=", 1)[1].strip()
                if v:
                    return v
    except Exception:
        pass
    return default


def main():
    ip = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_IP
    here = os.path.dirname(os.path.abspath(__file__))
    auto_client = here if os.path.isfile(os.path.join(here, "DOFLogin.exe")) else _ini_client(DEFAULT_CLIENT)
    client = sys.argv[2] if len(sys.argv) > 2 else os.environ.get("DNF_CLIENT", auto_client)

    if not re.match(r"^\d{1,3}(\.\d{1,3}){3}$", ip):
        print(f"IP 格式不对：{ip}")
        sys.exit(2)
    if not os.path.isdir(client):
        print(f"客户端目录不存在：{client}")
        sys.exit(2)

    print(f"客户端目录：{client}")
    print(f"目标 IP：{ip}")
    backup_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                              "备份", time.strftime("%Y%m%d-%H%M%S"))

    n = 0
    n += patch(os.path.join(client, "DLConfig.ini"), [
        (r"^DBHost\s*=.*$", f"DBHost={ip}"),
        (r"^GameHost\s*=.*$", f"GameHost={ip}"),
        (r"^AutoHosts\s*=.*$", "AutoHosts=True"),
    ], backup_dir)
    n += patch(os.path.join(client, "muqing.ini"), [
        (r"^服务器IP\s*=.*$", f"服务器IP={ip}"),
    ], backup_dir)
    # 85A 客户端多一个 DNFServer.ini（把 start.dnf.tw 重定向到 RedirectIP:7101）
    n += patch(os.path.join(client, "DNFServer.ini"), [
        (r"^RedirectIP\s*=.*$", f"RedirectIP={ip}"),
    ], backup_dir)

    print(f"\n共修改 {n} 处，备份在：{backup_dir}")
    print("如果登录器提示「Hosts修改失败」或白屏，请用管理员身份运行 DOFLogin.exe，")
    print("或者在工具里重跑一次【一键启动联机】会自动写 hosts。")


if __name__ == "__main__":
    main()
