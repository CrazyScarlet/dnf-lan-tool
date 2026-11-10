# -*- coding: utf-8 -*-
"""
DNF 服务端控制脚本（宿主机用）

用法：
    python dnf_server_ctl.py start    启动虚拟机 + 启动服务端
    python dnf_server_ctl.py stop     停止服务端
    python dnf_server_ctl.py status   查看状态

原理：
    1. 用 vmrun 启动 VMware 虚拟机（无界面模式），虚拟机里是 CentOS 7.6 + DNF 服务端；
    2. 等 SSH(22) 能连上后，通过 SSH 执行 /root/run 启动全部服务端组件；
    3. 最后检查 7101 / 10011 / 10186 等端口是否就绪（85A 端口集）。
"""
import os
import socket
import subprocess
import sys
import time

import paramiko

VMRUN = os.environ.get("DNF_VMRUN", r"D:\Program Files (x86)\VMware\VMware Workstation\vmrun.exe")
def _ini_value(key, default=""):
    """从工具根目录的 启动器配置.ini 里取键值（与 dnf_check.py 一致）。"""
    here = os.path.dirname(os.path.abspath(__file__))
    ini = os.path.join(os.path.dirname(here), "启动器配置.ini")
    try:
        with open(ini, "r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line or line.startswith("#") or "=" not in line:
                    continue
                k, v = line.split("=", 1)
                if k.strip().upper() == key.strip().upper():
                    return v.strip()
    except Exception:
        pass
    return default


VMX = (os.environ.get("DNF_VMX") or _ini_value("VMX", "") or
       r"E:\DNF8S3A3\CentOS 7.6\CentOS 7.6.vmx")

SSH_HOST = os.environ.get("DNF_SSH_HOST", "192.168.200.131")
SSH_PORT = int(os.environ.get("DNF_SSH_PORT", "22"))
SSH_USER = os.environ.get("DNF_SSH_USER", "root")
SSH_PWD = os.environ.get("DNF_SSH_PWD", "")

READY_PORTS = [7101, 10011, 10186]

# 假人系统（robot）部署在虚拟机 /root/robot，必须在启动服务端之前拉起来
ROBOT_BIN = "/root/robot"
ROBOT_LOG = "/root/config/logs/robot-stdout.log"
ROBOT_PORTS = [8111, 8112]


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


def _decode_out(raw):
    """尽力解码 vmrun 输出（不同环境分别是 UTF-8 / ANSI），仅用于打印。"""
    for enc in ("utf-8", "mbcs", "gbk"):
        try:
            return raw.decode(enc)
        except Exception:
            continue
    return raw.decode("utf-8", "replace")


def vm_running():
    try:
        proc = subprocess.run(
            [VMRUN, "-T", "ws", "list"],
            capture_output=True, timeout=60,
        )
        raw = proc.stdout or b""
    except Exception as e:
        print(f"[错误] 无法执行 vmrun: {e}")
        return False
    norm = lambda s: s.replace("/", "\\").strip().lower()
    target = norm(VMX)
    for enc in ("utf-8", "mbcs", "gbk"):
        try:
            text = raw.decode(enc)
        except Exception:
            continue
        if target in norm(text):
            return True
    return False


def wait_for(fn, what, timeout, interval=2):
    t0 = time.time()
    while time.time() - t0 < timeout:
        if fn():
            print(f"  √ {what} 就绪（用时 {int(time.time() - t0)} 秒）")
            return True
        time.sleep(interval)
    print(f"  × 等待 {what} 超时（{timeout} 秒）")
    return False


def ssh_exec(cmd, timeout=180):
    cli = paramiko.SSHClient()
    cli.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    cli.connect(SSH_HOST, SSH_PORT, SSH_USER, SSH_PWD, timeout=20, banner_timeout=30)
    try:
        stdin, stdout, stderr = cli.exec_command(cmd, timeout=timeout)
        out = stdout.read().decode("utf-8", "replace")
        err = stderr.read().decode("utf-8", "replace")
        rc = stdout.channel.recv_exit_status()
        return rc, out, err
    finally:
        cli.close()


def ssh_exec_bg(cmd, settle=2.0):
    """执行会派生出常驻进程的命令：不读到 EOF（否则会 PipeTimeout），
    发出去等一会儿就直接关连接，让 setsid/nohup 的子进程自己跑。"""
    cli = paramiko.SSHClient()
    cli.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    cli.connect(SSH_HOST, SSH_PORT, SSH_USER, SSH_PWD, timeout=20, banner_timeout=30)
    try:
        cli.exec_command(cmd, timeout=15)
        time.sleep(settle)
    finally:
        cli.close()


def robot_deployed():
    try:
        rc, out, _ = ssh_exec(f"test -x {ROBOT_BIN} && echo yes || echo no", timeout=30)
    except Exception:
        return False
    return out.strip() == "yes"


def robot_running():
    try:
        rc, out, _ = ssh_exec("pgrep -x robot >/dev/null 2>&1 && echo yes || echo no", timeout=30)
    except Exception:
        return False
    return out.strip() == "yes"


def robot_start(silent_if_running=False):
    print("[假人] 检查假人系统（./robot）……")
    if not robot_deployed():
        print(f"      未安装假人系统（{ROBOT_BIN} 不存在），跳过")
        return True
    if robot_running():
        if port_open(SSH_HOST, ROBOT_PORTS[0], 1.0):
            if not silent_if_running:
                print("      假人系统已经在运行，跳过启动")
            return True
        print("      假人进程残留但没监听端口（可能正在退出），先清理再重启")
        try:
            ssh_exec_bg("pkill -x robot; pkill -f '/root/robot'; true", settle=1.0)
        except Exception:
            pass
        for _ in range(20):
            if not robot_running():
                break
            time.sleep(1)
        time.sleep(1.0)
    print("      正在启动假人系统（后台执行 ./robot）……")
    try:
        ssh_exec_bg(f"cd /root && setsid nohup ./robot > {ROBOT_LOG} 2>&1 < /dev/null &", settle=2.5)
    except Exception as e:
        print(f"  × 启动假人系统失败: {e}")
        return False
    if not wait_for(lambda: port_open(SSH_HOST, ROBOT_PORTS[0]), f"假人端口 {ROBOT_PORTS[0]}", 60):
        print("  × 假人系统没起来（后面会继续启动服务端，可稍后在 Web 页面看状态）")
        return False
    for port in ROBOT_PORTS[1:]:
        wait_for(lambda p=port: port_open(SSH_HOST, p), f"假人端口 {port}", 20)
    print("      √ 假人系统已启动（Web 管理页 http://%s:%d）" % (SSH_HOST, ROBOT_PORTS[1]))
    return True


def robot_stop():
    try:
        ssh_exec("pkill -x robot >/dev/null 2>&1; sleep 1; "
                 "pgrep -f '/root/robot' >/dev/null 2>&1 && pkill -f '/root/robot'; true", timeout=40)
    except Exception:
        return False
    time.sleep(1)
    return not robot_running()


def vm_start():
    if vm_running():
        print("[1/4] 虚拟机已经在运行，跳过启动")
        return True
    print("[1/4] 正在启动虚拟机（无界面模式，约 30~60 秒）……")
    try:
        r = subprocess.run(
            [VMRUN, "-T", "ws", "start", VMX, "nogui"],
            capture_output=True, timeout=300,
        )
    except Exception as e:
        print(f"[错误] 启动虚拟机失败: {e}")
        return False
    if r.returncode != 0:
        print(f"[错误] vmrun 返回 {r.returncode}: {_decode_out((r.stdout or b'') + (r.stderr or b''))}")
        return False
    print("      虚拟机已启动")
    return True


def server_start(with_robot=True):
    print("[1/4] 等待虚拟机 SSH 服务……")
    if not wait_for(lambda: port_open(SSH_HOST, SSH_PORT), "SSH(22)", 180):
        return False

    if with_robot:
        # 假人系统必须在服务端起来之前启动（用户手工顺序：先 ./robot 再 /root/run）
        robot_start()

    print("[2/4] 检查服务端进程……")
    try:
        rc, out, _ = ssh_exec("pgrep -c df_channel_r || true", timeout=30)
    except Exception as e:
        print(f"[错误] SSH 连接失败: {e}")
        return False
    running = out.strip() not in ("", "0")
    if running:
        # 85A 上 df_game_r(siroco11) 单独崩溃时 df_channel_r 仍然活着，
        # 只看进程会误判「已在运行」而不拉起；必须用游戏端口 10011 兜底判断。
        try:
            game_ok = port_open(SSH_HOST, 10011)
        except Exception:
            game_ok = False
        if not game_ok:
            print("      检测到游戏端口 10011 未监听（df_game_r 掉了），重拉服务端……")
            try:
                ssh_exec("sh /root/stop >/dev/null 2>&1; sleep 2", timeout=120)
            except Exception:
                pass
            running = False
    if running:
        print("      服务端已在运行，跳过启动")
    else:
        print("      正在启动服务端（后台执行 /root/run）……")
        cmd = "cd /root && setsid bash /root/run > /root/run-last.log 2>&1 < /dev/null &"
        try:
            ssh_exec(cmd, timeout=30)
        except Exception as e:
            print(f"[错误] 启动命令执行失败: {e}")
            return False

    print("[3/4] 等待服务端端口就绪（约 20~40 秒）……")
    ok = True
    for port in READY_PORTS:
        ok = wait_for(lambda p=port: port_open(SSH_HOST, p), f"端口 {port}", 150) and ok

    print("[4/4] 检查假人系统……")
    if with_robot and robot_deployed():
        if robot_running():
            print("      √ 假人系统在运行（游戏服端口就绪后会自动开始上号）")
        else:
            print("      × 假人系统没在运行，可以重开一次【一键启动联机】")
    return ok


def server_stop():
    print("[1/2] 检查虚拟机……")
    if not vm_running():
        print("      虚拟机未运行，无需停止")
        return True
    print("[2/2] 正在停止服务端（执行 /root/stop）……")
    try:
        ssh_exec("bash /root/stop > /dev/null 2>&1", timeout=120)
    except Exception as e:
        print(f"[错误] 停止失败: {e}")
        return False
    time.sleep(3)
    left = [p for p in READY_PORTS if port_open(SSH_HOST, p)]
    if left:
        print(f"      仍有端口在监听: {left}")
        return False
    if robot_running():
        print("      正在停止假人系统……")
        robot_stop()
    print("      √ 服务端已停止（虚拟机仍在运行）")
    return True


def server_status():
    vm = vm_running()
    print(f"虚拟机: {'运行中' if vm else '未运行'}")
    if vm:
        for port in READY_PORTS + [7200, 3306]:
            print(f"  端口 {port}: {'监听中' if port_open(SSH_HOST, port) else '未监听'}")
        if robot_deployed():
            print(f"  假人系统: {'运行中' if robot_running() else '未运行'}"
                  f"（Web 管理页 http://{SSH_HOST}:{ROBOT_PORTS[1]}）")
        else:
            print("  假人系统: 未安装")
    return vm


def parse_robot_flag(argv):
    """--robot / --no-robot 覆盖默认（默认开）。"""
    flag = True
    for a in argv:
        if a == "--no-robot":
            flag = False
        elif a == "--robot":
            flag = True
    return flag


def main():
    args = sys.argv[1:]
    action = args[0].lower() if args else "status"
    with_robot = parse_robot_flag(args)
    if action == "start":
        if not vm_start():
            sys.exit(1)
        sys.exit(0 if server_start(with_robot) else 1)
    elif action == "robot":
        sys.exit(0 if robot_start() else 1)
    elif action == "stop":
        sys.exit(0 if server_stop() else 1)
    elif action == "status":
        server_status()
        sys.exit(0)
    elif action == "restart":
        server_stop()
        time.sleep(2)
        if not vm_start():
            sys.exit(1)
        sys.exit(0 if server_start(with_robot) else 1)
    else:
        print("用法: python dnf_server_ctl.py start|stop|restart|status [--robot|--no-robot]")
        sys.exit(2)


if __name__ == "__main__":
    main()
