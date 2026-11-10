#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""拍卖行 / 金币寄售 服务控制（重启后重建搜索索引）

背景：df_auction_r 只在启动时把 auction_main 全量读进内存建搜索索引，
      假人（robot）补货是直接写数据库，不重启服务就搜不到新挂单。

用法：
  python dnf_auction_ctl.py restart     # 重启 auction(30803) + point(30603)
  python dnf_auction_ctl.py status      # 看进程 / 端口 / 最近重启日志 / 定时任务

SSH 参数走环境变量：DNF_SSH_HOST / DNF_SSH_PORT / DNF_SSH_USER / DNF_SSH_PWD
"""
import os
import sys

sys.dont_write_bytecode = True

HOST = os.environ.get("DNF_SSH_HOST", "192.168.200.131")
PORT = int(os.environ.get("DNF_SSH_PORT", "22"))
USER = os.environ.get("DNF_SSH_USER", "root")
PASSWORD = os.environ.get("DNF_SSH_PWD", "")

REMOTE_SCRIPT = "/root/restart_auction.sh"
REMOTE_LOG = "/root/restart_auction.log"
PORTS = [30803, 30603]


def out(msg):
    sys.stdout.write(str(msg) + "\n")
    sys.stdout.flush()


def connect():
    import paramiko
    c = paramiko.SSHClient()
    c.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    c.connect(HOST, PORT, USER, PASSWORD, timeout=20, banner_timeout=20, auth_timeout=20)
    return c


def run(c, cmd, timeout=180):
    stdin, stdout, stderr = c.exec_command(cmd, timeout=timeout)
    o = stdout.read().decode("utf-8", "replace")
    e = stderr.read().decode("utf-8", "replace")
    stdin.close()
    return o + e


def check_ports(c):
    res = run(c, "ss -ltn 2>/dev/null | grep -E ':(30803|30603)'")
    return res.strip()


def do_restart():
    c = connect()
    try:
        if run(c, "test -x %s && echo yes || echo no" % REMOTE_SCRIPT).strip() != "yes":
            out("× 服务器上没有 %s，无法重启（请先部署脚本）" % REMOTE_SCRIPT)
            return 2
        out("正在重启拍卖行 / 金币寄售（约 10 秒，玩家无感）……")
        res = run(c, "sh %s 2>&1" % REMOTE_SCRIPT, timeout=180)
        for line in res.splitlines():
            if line.strip():
                out("  " + line.strip())
        ports = check_ports(c)
        ok = ports.count("30803") >= 1 and ports.count("30603") >= 1
        out("")
        out("√ 已重启：拍卖行 30803 / 金币寄售 30603" if ok else "× 端口未全部监听，请看上面的输出")
        out("搜索索引已按当前 auction_main 重建，新补的货现在能搜到。")
        return 0 if ok else 1
    finally:
        c.close()


def do_status():
    c = connect()
    try:
        out("== 拍卖行 / 金币寄售 状态 ==")
        ps = run(c, "ps -eo pid,lstart,cmd | grep -E 'df_auction_r|df_point_r' | grep -v grep").strip()
        out(ps if ps else "  × 两个服务都没在跑")
        ports = check_ports(c)
        for p in PORTS:
            out("  端口 %d：%s" % (p, "√ 监听中" if (":%d" % p) in ports else "× 未监听"))
        log = run(c, "tail -4 %s 2>/dev/null" % REMOTE_LOG).strip()
        out("  最近重启记录：")
        out("    " + (log.replace("\n", "\n    ") if log else "（暂无）"))
        cron = run(c, "crontab -l 2>/dev/null | grep restart_auction || true").strip()
        out("  定时任务：%s" % (cron if cron else "× 未安装（不会有 30 分钟自动重启）"))
        return 0
    finally:
        c.close()


def main():
    if len(sys.argv) < 2:
        out(__doc__)
        return 2
    cmd = sys.argv[1].lower()
    try:
        if cmd == "restart":
            return do_restart()
        if cmd == "status":
            return do_status()
        out("未知命令：%s" % cmd)
        return 2
    except Exception as ex:
        out("× 出错了：%s" % ex)
        return 1


if __name__ == "__main__":
    sys.exit(main())
