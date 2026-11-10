# -*- coding: utf-8 -*-
"""假人参数（/root/config/conf/robot_config.ini）读写。

用法：
  python dnf_robot_config.py get --out <本地文件>
  python dnf_robot_config.py put --in <本地文件> [--restart]
"""
import io
import os
import posixpath
import sys
import time

sys.path.insert(
    0,
    os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "python", "Lib", "site-packages"),
)

import paramiko

HOST = os.environ.get("DNF_SSH_HOST", "192.168.200.131")
PORT = int(os.environ.get("DNF_SSH_PORT", "22"))
USER = os.environ.get("DNF_SSH_USER", "root")
PASSWORD = os.environ.get("DNF_SSH_PWD", "")

CONFIG_DIR = "/root/config"
ROBOT_CONF = "/root/config/conf/robot_config.ini"
ROBOT_LOG = "/root/config/logs/robot-stdout.log"
ROBOT_DIR = "/root"


def out(msg):
    sys.stdout.write(msg + "\n")
    sys.stdout.flush()


def connect():
    c = paramiko.SSHClient()
    c.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    c.connect(HOST, PORT, USER, PASSWORD, timeout=12, banner_timeout=12, auth_timeout=12)
    return c


def run(client, cmd, timeout=20):
    stdin, stdout, stderr = client.exec_command(cmd, timeout=timeout)
    o = stdout.read().decode("utf-8", "replace")
    e = stderr.read().decode("utf-8", "replace")
    rc = stdout.channel.recv_exit_status()
    return rc, o, e


def run_bg(client, cmd, settle=2.0):
    """后台起进程：发命令后不读输出，短暂等待后直接关通道。"""
    try:
        client.exec_command(cmd, timeout=5)
    except Exception:
        pass
    time.sleep(settle)


def do_get(path):
    c = connect()
    try:
        sftp = c.open_sftp()
        try:
            data = sftp.open(ROBOT_CONF).read()
        finally:
            sftp.close()
        io.open(path, "wb").write(data)
        out("√ 已读取 %s（%d 字节）" % (ROBOT_CONF, len(data)))
        return 0
    finally:
        c.close()


def do_put(path, restart):
    if not os.path.isfile(path):
        out("× 找不到本地文件：%s" % path)
        return 2
    data = io.open(path, "rb").read()
    if not data.strip():
        out("× 本地文件是空的，已取消")
        return 2
    c = connect()
    try:
        run(c, "cp -f %s %s.bak.$(date +%%Y%%m%%d-%%H%%M%%S) 2>/dev/null; true" % (ROBOT_CONF, ROBOT_CONF))
        out("√ 已备份原配置")
        sftp = c.open_sftp()
        try:
            sftp.putfo(io.BytesIO(data), ROBOT_CONF)
        finally:
            sftp.close()
        run(c, "chmod 600 %s; true" % ROBOT_CONF)
        out("√ 已写入 %s" % ROBOT_CONF)
        if restart:
            out("正在重启假人系统 ……")
            run(c, "pkill -x robot 2>/dev/null; pkill -f '/root/robot' 2>/dev/null; true")
            # 等旧进程彻底退出（否则 5063/8111 还占着，新实例会 bind 失败）
            for _ in range(20):
                rc, o, e = run(c, "pgrep -x robot | wc -l")
                if o.strip() in ("", "0"):
                    break
                time.sleep(1)
            time.sleep(1.5)
            run(c, "mkdir -p %s" % posixpath.dirname(ROBOT_LOG))
            run_bg(c, "cd %s && setsid nohup ./robot > %s 2>&1 < /dev/null &" % (ROBOT_DIR, ROBOT_LOG), 2.5)
            ok = False
            for _ in range(60):
                rc, o, e = run(c, "ss -ltn 2>/dev/null | grep -c ':8111 ' || true")
                if o.strip() not in ("", "0"):
                    ok = True
                    break
                time.sleep(1)
            if ok:
                out("√ 假人系统已重启（8111 就绪）")
            else:
                out("× 假人系统没起来，看日志：%s" % ROBOT_LOG)
                return 3
        else:
            out("√ 已保存（没重启假人，新参数会在假人重启后全部生效）")
        return 0
    finally:
        c.close()


def main(argv):
    if len(argv) < 2:
        out("用法：dnf_robot_config.py get --out <文件> | put --in <文件> [--restart]")
        return 1
    action = argv[1]
    path = None
    restart = "--restart" in argv
    for i, a in enumerate(argv):
        if a in ("--out", "--in") and i + 1 < len(argv):
            path = argv[i + 1]
    if not path:
        out("× 缺少 --out / --in 参数")
        return 1
    try:
        if action == "get":
            return do_get(path)
        if action == "put":
            return do_put(path, restart)
        out("× 未知动作：%s" % action)
        return 1
    except Exception as ex:
        out("× 连接服务端失败：%s" % ex)
        return 4


if __name__ == "__main__":
    sys.exit(main(sys.argv))
