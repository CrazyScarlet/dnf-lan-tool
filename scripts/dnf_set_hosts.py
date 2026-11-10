# -*- coding: utf-8 -*-
"""
写入 hosts：把 start.dnf.tw 指向指定 IP（需要管理员权限）。

用法：
    python dnf_set_hosts.py                # 指向 你的联机 IP
    python dnf_set_hosts.py 你的联机 IP
"""
import ctypes
import os
import re
import stat
import sys

DEFAULT_IP = os.environ.get("DNF_HOST_IP", "")
HOSTS = r"C:\Windows\System32\drivers\etc\hosts"
MARK = "start.dnf.tw"


def is_admin():
    try:
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except Exception:
        return False


def main():
    ip = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_IP
    if not is_admin():
        print("需要管理员权限！本脚本一般由工具自动调用（会提权）。")
        sys.exit(1)

    with open(HOSTS, "r", encoding="utf-8", errors="ignore") as f:
        lines = f.readlines()

    kept = [l for l in lines if MARK not in l]
    # 去掉文件末尾多余空行
    while kept and not kept[-1].strip():
        kept.pop()
    kept.append(f"\n{ip} {MARK}\n")

    # hosts 常被登录器设成「只读」，只读属性会让管理员也写不进去（Errno 13）
    try:
        readonly = not bool(os.stat(HOSTS).st_mode & stat.S_IWRITE)
    except OSError:
        readonly = False
    if readonly:
        os.chmod(HOSTS, stat.S_IWRITE)
        print("（hosts 是只读属性，已临时解除）")

    with open(HOSTS, "w", encoding="utf-8") as f:
        f.writelines(kept)

    if readonly:
        os.chmod(HOSTS, stat.S_IREAD)
        print("（已恢复只读属性）")

    print(f"√ hosts 已更新：{ip} {MARK}")


if __name__ == "__main__":
    main()
