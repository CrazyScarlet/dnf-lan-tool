#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""假人系统（robot）一键部署工具

用法:
    dnf_robot_deploy.py check
    dnf_robot_deploy.py deploy [--pkg DIR] [--client DIR] [--no-start]

动作:
  1. 连接虚拟机 (root@192.168.200.131:22)
  2. 停止正在运行的 robot
  3. 备份旧 /root/robot -> /root/robot.bak.<时间>
  4. SFTP 上传部署包里的 robot 到 /root/robot.new -> chmod +x -> mv 到 /root/robot
  5. 若 /root/config/conf/config.ini 不存在: 建目录并上传 config/conf/config.ini 与 config/keys/*.pem
  6. 启动 robot 并等待 8111 / 8112 端口就绪
  7. 把 /root/config/keys/*.pem 复制到 /home/neople/game，并下载到本机客户端目录
"""
import os
import posixpath
import socket
import sys
import time

import paramiko

HOST = os.environ.get("DNF_SSH_HOST", "192.168.200.131")
PORT = int(os.environ.get("DNF_SSH_PORT", "22"))
USER = os.environ.get("DNF_SSH_USER", "root")
PASSWORD = os.environ.get("DNF_SSH_PWD", "")

DEFAULT_PKG = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "假人部署包")
def _ini_client():
    try:
        ini = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "启动器配置.ini")
        for line in open(ini, encoding="utf-8"):
            if line.startswith("CLIENTDIR="):
                return line.split("=", 1)[1].strip()
    except Exception:
        pass
    return os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "客户端")
DEFAULT_CLIENT = _ini_client()

ROBOT_BIN = "/root/robot"
ROBOT_DIR = "/root"
CONFIG_DIR = "/root/config"
CONF_FILE = "/root/config/conf/config.ini"
KEYS_DIR = "/root/config/keys"
TEMPLATES_DIR = "/root/config/templates"
GAME_DIR = os.environ.get("DNF_GAMEDIR", "/home/neople/game")
ROBOT_LOG = "/root/config/logs/robot-stdout.log"
ROBOT_PORTS = [8111, 8112]


def out(msg=""):
    try:
        print(msg, flush=True)
    except Exception:
        print(msg.encode("gbk", "replace").decode("gbk", "replace"), flush=True)


def connect(timeout=15):
    c = paramiko.SSHClient()
    c.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    c.connect(HOST, PORT, USER, PASSWORD, timeout=timeout, banner_timeout=timeout,
              auth_timeout=timeout)
    return c


def run(client, cmd, timeout=60):
    stdin, stdout, stderr = client.exec_command(cmd, timeout=timeout)
    o = stdout.read().decode("utf-8", "replace")
    e = stderr.read().decode("utf-8", "replace")
    rc = stdout.channel.recv_exit_status()
    return rc, o, e


def run_bg(client, cmd, settle=2.0):
    """发命令后不等输出（后台常驻进程会让 stdout 一直不 EOF）"""
    try:
        stdin, stdout, stderr = client.exec_command(cmd, timeout=10)
    except Exception:
        return
    time.sleep(settle)
    for stream in (stdin, stdout, stderr):
        try:
            stream.close()
        except Exception:
            pass


def port_open(host, port, timeout=1.5):
    s = socket.socket()
    s.settimeout(timeout)
    try:
        s.connect((host, port))
        return True
    except Exception:
        return False
    finally:
        try:
            s.close()
        except Exception:
            pass


def wait_port(host, port, seconds=60, label=""):
    start = time.time()
    while time.time() - start < seconds:
        if port_open(host, port, 1.0):
            out("  √ %s 就绪（用时 %d 秒）" % (label or ("端口 %d" % port), int(time.time() - start)))
            return True
        time.sleep(1)
    out("  × %s 等待超时（%d 秒）" % (label or ("端口 %d" % port), seconds))
    return False


def is_deployed(client):
    rc, o, e = run(client, "test -x %s && echo yes || echo no" % ROBOT_BIN)
    return o.strip() == "yes"


def robot_running(client):
    rc, o, e = run(client, "pgrep -x robot | head -1")
    return bool(o.strip())


def stop_robot(client):
    run(client, "pkill -TERM -x robot 2>/dev/null; sleep 1; pkill -9 -x robot 2>/dev/null; true")
    time.sleep(1)
    return not robot_running(client)


def upload_robot(client, pkg):
    local = os.path.join(pkg, "robot")
    if not os.path.isfile(local):
        out("  × 部署包里找不到 robot 文件：%s" % local)
        return False
    size = os.path.getsize(local)
    out("  上传 %s（%.1f MB）……" % (os.path.basename(local), size / 1024.0 / 1024.0))
    sftp = client.open_sftp()
    tmp = ROBOT_BIN + ".new"
    last = [0]

    def cb(done, total):
        pct = int(done * 100 / max(total, 1))
        if pct >= last[0] + 20 or (pct >= 100 and last[0] < 100):
            last[0] = pct
            out("    %d%%" % pct)

    try:
        sftp.put(local, tmp, callback=cb)
    finally:
        try:
            sftp.close()
        except Exception:
            pass
    run(client, "chmod 755 %s" % tmp)
    if is_deployed(client):
        rc, o, e = run(client, "cp -a %s %s.bak.%s && echo ok" % (ROBOT_BIN, ROBOT_BIN, time.strftime("%Y%m%d-%H%M%S")))
        out("  已备份旧版本：%s" % (o.strip() or "ok"))
    rc, o, e = run(client, "mv -f %s %s && ls -l %s" % (tmp, ROBOT_BIN, ROBOT_BIN))
    out("  √ robot 已就位：%s" % o.strip())
    return True


def upload_config(client, pkg):
    rc, o, e = run(client, "test -f %s && echo yes || echo no" % CONF_FILE)
    if o.strip() == "yes":
        out("  已有配置，保留：%s" % CONF_FILE)
        return True
    out("  未发现配置，开始初始化 /root/config …")
    run(client, "mkdir -p %s/conf %s/keys %s/logs %s/tmp %s/pvf %s/state %s/templates"
        % (CONFIG_DIR, CONFIG_DIR, CONFIG_DIR, CONFIG_DIR, CONFIG_DIR, CONFIG_DIR, CONFIG_DIR))
    sftp = client.open_sftp()
    try:
        local_conf = os.path.join(pkg, "config", "conf", "config.ini")
        if not os.path.isfile(local_conf):
            out("  × 部署包里找不到 config\\conf\\config.ini")
            return False
        sftp.put(local_conf, CONF_FILE)
        out("  √ 已上传 config.ini")
        keydir = os.path.join(pkg, "config", "keys")
        if os.path.isdir(keydir):
            for name in os.listdir(keydir):
                if name.lower().endswith(".pem"):
                    sftp.put(os.path.join(keydir, name), posixpath.join(KEYS_DIR, name))
                    out("  √ 已上传密钥 %s" % name)
    finally:
        try:
            sftp.close()
        except Exception:
            pass
    run(client, "chmod 600 %s/config.ini; chmod 600 %s/*.pem 2>/dev/null; true" % (CONFIG_DIR, KEYS_DIR))
    return True


def upload_templates(client, pkg):
    """每次部署都覆盖喊话模板等 templates 文件（robot 自带的默认喊话内容不合适）。"""
    tdir = os.path.join(pkg, "config", "templates")
    if not os.path.isdir(tdir):
        return True
    names = [n for n in os.listdir(tdir) if n.lower().endswith(".json")]
    if not names:
        return True
    run(client, "mkdir -p %s" % TEMPLATES_DIR)
    sftp = client.open_sftp()
    try:
        for name in names:
            sftp.put(os.path.join(tdir, name), posixpath.join(TEMPLATES_DIR, name))
            out("  √ 已更新模板 %s" % name)
    finally:
        try:
            sftp.close()
        except Exception:
            pass
    return True


def start_robot(client):
    run(client, "mkdir -p %s" % posixpath.dirname(ROBOT_LOG))
    run_bg(client, "cd %s && setsid nohup ./robot > %s 2>&1 < /dev/null &" % (ROBOT_DIR, ROBOT_LOG), 2.5)
    ok = wait_port(HOST, 8111, 60, "假人端口 8111")
    ok2 = wait_port(HOST, 8112, 20, "假人管理页 8112")
    return ok and ok2


def sync_keys(client, client_dir):
    rc, o, e = run(client, "ls -1 %s/*.pem 2>/dev/null" % KEYS_DIR)
    names = [x.strip() for x in o.splitlines() if x.strip()]
    if not names:
        out("  × 服务端没有找到密钥文件（%s）" % KEYS_DIR)
        return False
    # 1) 复制到游戏服务端目录
    run(client, "cp -f %s/*.pem %s/ 2>/dev/null; true" % (KEYS_DIR, GAME_DIR))
    rc, o2, e = run(client, "ls -1 %s/*.pem 2>/dev/null | wc -l" % GAME_DIR)
    out("  √ 已复制到游戏服务端目录 %s（现有 %s 个 pem）" % (GAME_DIR, o2.strip()))
    # 2) 下载到本机客户端目录
    if client_dir and os.path.isdir(client_dir):
        sftp = client.open_sftp()
        try:
            for remote in names:
                base = posixpath.basename(remote)
                sftp.get(remote, os.path.join(client_dir, base))
                out("  √ 已下载到客户端：%s" % os.path.join(client_dir, base))
        finally:
            try:
                sftp.close()
            except Exception:
                pass
    else:
        out("  ! 客户端目录不存在，跳过下载：%s" % client_dir)
    return True


def do_check(pkg):
    out("== 检查假人系统部署状态 ==")
    out("  部署包：%s" % pkg)
    if not os.path.isdir(pkg):
        out("  × 部署包目录不存在")
        return 2
    for name in ("robot", os.path.join("config", "conf", "config.ini")):
        p = os.path.join(pkg, name)
        out("  %s %s" % ("√" if os.path.exists(p) else "×", p))
    try:
        c = connect()
    except Exception as ex:
        out("  × 连接服务端失败：%s" % ex)
        return 3
    try:
        dep = is_deployed(c)
        running = robot_running(c)
        rc, o, e = run(c, "test -f %s && echo yes || echo no" % CONF_FILE)
        hasconf = o.strip() == "yes"
        rc, o, e = run(c, "ls -1 %s/*.pem 2>/dev/null | wc -l" % GAME_DIR)
        gamekeys = o.strip()
        out("  服务端 %s: robot 已部署=%s，配置=%s，进程=%s，8111=%s，游戏目录 pem=%s"
            % (HOST, "是" if dep else "否", "有" if hasconf else "无",
               "在跑" if running else "没跑",
               "通" if port_open(HOST, 8111) else "不通", gamekeys))
    finally:
        c.close()
    return 0


def do_deploy(pkg, client_dir, start=True):
    out("== 部署假人系统（robot）==")
    out("  部署包：%s" % pkg)
    out("  客户端：%s" % client_dir)
    if not os.path.isdir(pkg):
        out("  × 部署包目录不存在：%s" % pkg)
        return 2

    out("[1/6] 连接服务端 %s ……" % HOST)
    try:
        c = connect()
    except Exception as ex:
        out("  × 连接失败：%s" % ex)
        return 3
    out("  √ 已连接")

    try:
        out("[2/6] 停止正在运行的假人 ……")
        if stop_robot(c):
            out("  √ 已停止（或本来就没运行）")

        out("[3/6] 上传 robot 程序 ……")
        if not upload_robot(c, pkg):
            return 4

        out("[4/6] 部署配置与密钥 ……")
        if not upload_config(c, pkg):
            return 5
        upload_templates(c, pkg)

        out("[5/6] 同步密钥到游戏服务端与客户端 ……")
        sync_keys(c, client_dir)

        if start:
            out("[6/6] 启动假人系统 ……")
            if not start_robot(c):
                out("  × 假人系统启动失败，看日志：%s" % ROBOT_LOG)
                return 6
            out("  √ 假人系统已启动")
            out("    管理页：http://%s:8112   密码见 robot 配置" % HOST)
        else:
            out("[6/6] 已跳过启动（--no-start）")
        out("")
        out("部署完成。")
        out("下一步：进游戏前点【一键启动联机】（假人会自动先起来），")
        out("       然后在【假人管理】里登录（密码见 robot 配置）→ 密钥 → 应用密钥。")
    finally:
        try:
            c.close()
        except Exception:
            pass
    return 0


def main(argv):
    if len(argv) < 2 or argv[1] in ("-h", "--help", "help"):
        out(__doc__)
        return 1
    cmd = argv[1]
    pkg = os.environ.get("DNF_ROBOTPKG", DEFAULT_PKG)
    client_dir = os.environ.get("DNF_CLIENT", DEFAULT_CLIENT)
    start = True
    i = 2
    while i < len(argv):
        a = argv[i]
        if a == "--pkg" and i + 1 < len(argv):
            pkg = argv[i + 1]
            i += 2
        elif a == "--client" and i + 1 < len(argv):
            client_dir = argv[i + 1]
            i += 2
        elif a == "--no-start":
            start = False
            i += 1
        else:
            i += 1

    if cmd == "check":
        return do_check(pkg)
    if cmd == "deploy":
        return do_deploy(pkg, client_dir, start)
    out("未知命令：%s" % cmd)
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
