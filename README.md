> 假人系统来自开源项目 **[zmbksyyr/dnf-robot-s4a21](https://github.com/zmbksyyr/dnf-robot-s4a21)**。本仓库**不包含**该项目的源码，只放联机工具本体，并在下方给出它的获取、编译与使用说明。

# DNF 联机工具（dnf-lan-tool）

把朋友通过 **Radmin VPN** 连到你本机 **VMware** 里的 DNF 服务端：一键起服、端口转发、
客户端配置、假人系统部署与管理，全部收在一个 WinForms 小工具里。

> 仅用于网络协议学习与私服运维研究；仓库不含游戏客户端、服务端二进制或账号信息。

## 适用版本

假人系统在以下游戏版本上实测可用：

| # | 版本 | 说明 |
|---|---|---|
| ① | **70S2A1** | LK 大佬 1 月份版本 |
| ② | **70S2A3** | 肥波佬版本，本体为退网佬的 70S2A1 + 70S2A3 内容 |
| ③ | **70S3A3** | 退网佬「异界风云」 |
| ④ | **85A** | 嘎嘎香等一众大佬修复版。**假人走路、交易、摆摊尚未修复** |

> 上述版本中，假人的登录、出生分布、跨区穿门、分解机摊位、全服喇叭、组队邀请均已可用；
> 85A 下假人的行走位移与个人商店受客户端加密限制，详见下文「已知限制」。

## 游戏资源

本仓库**不提供**任何游戏资源。客户端、服务端、PVF 等**全部来自「台服 DNF 吧」**，
请自行到该处获取，并遵守其发布说明。本仓库只包含联机工具本身的源码。

## 功能

**联机**
- 房主/朋友双模式：房主一条龙起服（hosts → 假人 → 虚拟机 → 端口转发 → 自检），朋友只改客户端指向
- 内置端口转发（游戏 10011/11011、拍卖行 30803、金币寄售 30603），不需要额外的转发软件
- 一键改联机 IP：自动改服务端 37 处配置 + 客户端 `DLConfig.ini` / `muqing.ini` / hosts
- 一键检查状态：五项自检，红了直接看日志定位

**假人系统**（可选，房主端；二进制来自上游项目，见下文）
- 部署、重启、清理、上下线、移动、喊话、摆摊
- 原生管理面板：假人列表 / 在线数 / 城镇区域坐标 / 摆摊状态，5 秒刷新
- 参数面板：可视化修改 `robot_config.ini`（等级、职业、出生城镇、摆摊、喊话、自动调度）
- 市场面板：拍卖行与金币寄售服务的补货、收菜、清库、守卫、重启索引

## 快速开始

```
房主：Radmin VPN 入网 → 双击工具 → 我是房主 → 一键启动联机 → 启动游戏
朋友：Radmin VPN 入网 → 同一个 exe → 我不是房主 → 填房主 IP → 一键启动联机
```

详细步骤见 [docs/快速开始.txt](docs/快速开始.txt) 与 [docs/联机教程.txt](docs/联机教程.txt)。

## 目录结构

```
dnf-lan-tool/
├─ launcher/                 启动器（C# / WinForms，单文件源码）
│  ├─ DNF联机工具源码.cs
│  └─ build.bat              用 .NET Framework 自带 csc 编译
├─ scripts/                  运维脚本（Python 3 + paramiko）
│  ├─ dnf_check.py           五项自检
│  ├─ dnf_server_ctl.py      启停虚拟机与服务端
│  ├─ dnf_proxy.py / dnf_proxy_ctl.py   端口转发
│  ├─ dnf_robot_deploy.py    假人部署 / 重启 / 清理（上传你自己编的上游二进制）
│  ├─ dnf_robot_config.py    读写 robot_config.ini
│  ├─ dnf_auction_ctl.py     拍卖行重启与状态
│  └─ dnf_set_ip.py / dnf_set_hosts.py / dnf_set_server_ip.py   换 IP
├─ config/                   示例配置（不含真实 IP 与口令）
├─ docs/                     使用说明、改造记录、协议笔记
├─ LICENSE                   MIT
└─ README.md
```

## 构建

### 启动器

需要 Windows 自带的 .NET Framework 4.x（Win10/11 默认就有），无需 Visual Studio：

```bat
cd launcher
build.bat
```

`build.bat` 实际调用：

```bat
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  /out:DnfLanTool.exe "DNF联机工具源码.cs"
```

### 假人系统

见下一节；源码不在本仓库。

## 假人系统（上游项目）

假人（Robot）是一套独立的 Go 程序，托管在上游仓库：

**https://github.com/zmbksyyr/dnf-robot-s4a21**

我们**没有**把它的源码放进本仓库，原因是上游仓库没有声明授权许可证
（GitHub 显示 `license: null`，根目录也没有 LICENSE 文件）。本仓库只在
[docs/假人系统改造记录.txt](docs/假人系统改造记录.txt) 里记录了针对 85A 服务端的适配经验，
可自行按图索骥。

### 1. 获取源码

```bash
git clone https://github.com/zmbksyyr/dnf-robot-s4a21.git dnf-robot
cd dnf-robot
```

### 2. 交叉编译（在 Windows 上编 Linux 二进制）

```bat
set GOOS=linux
set GOARCH=amd64
set GOPROXY=https://goproxy.cn,direct
go build -trimpath -buildvcs=false -o robot-linux ./cmd/robot
```

需要 Go 1.21 或更高（`go version` 确认）。

### 3. 部署到服务端

```bash
scp robot-linux root@<VM_IP>:/root/robot.new
ssh root@<VM_IP>
# 在 VM 上：
pkill -9 -x robot; sleep 2
cp /root/robot /root/robot.prev
cp /root/robot.new /root/robot && chmod +x /root/robot
cd /root && DNF_MEGA_SPACE=0 DNF_MEGA_POS=66 setsid ./robot
```

首次运行前确认 `/root/config/conf/config.ini` 里的 `db_host` / `db_user` / `db_password`
与你的服务端一致；`/root/config/keys/` 下的 `publickey.pem` / `privatekey.pem` 从客户端目录取。

### 4. 配置 `robot_config.ini`

文件在服务端 `/root/config/conf/robot_config.ini`，常用项（以文件里的实际字段为准）：

| 键 | 作用 | 典型值 |
| --- | --- | --- |
| `robot_uid_start` / `robot_uid_end` | 假人账号号段 | `17000000` / `17004999` |
| `auto_target_online_count` | 目标在线假人数 | `150`（超过 200 容易打爆登录队列） |
| `spawn_area` | 出生区域；`-1` 表示随机 | `-1` |
| `spawn_x_min` / `spawn_x_max` 等 | 出生点矩形 | `0` / `4000` / `0` / `1000` |
| `follow_account` | 填账号则跟随该账号所在城镇；留空＝全城镇随机 | 空 |
| `auto_move_interval_min_sec` / `_max_sec` | 移动间隔 | `6` / `18` |

改完在工具里点【重启假人】即可，不需要重装。

### 5. 管理面板

服务端 `http://<VM_IP>:8112`，默认账号 **admin / twadmin**，**部署后请立刻改掉**。
本工具的「假人管理」页读的就是这个面板的接口。

### 6. 我们做过的适配

`docs/假人系统改造记录.txt` 覆盖了这些坑（都是针对这台 85A 实测出来的）：

- 出生分布：按区域矩形面积加权，避免全挤在 0 区 / 一个城镇
- `SET_USER_AREA` 字段顺序、必须补齐到 16 字节，否则加密层静默失败
- 登录限速 + 30 秒看门狗：并发一高就会卡在 `StateLogin`，只能 1 秒登 1 个
- `charac_stat.visible_flags` 的 bit0：不置位服务端只把移动包回给本人
- 摆摊走 `CMD 234`（分解机），成功判据是 `NOTI 206`
- 全服喇叭 `chat_type 15`，需要背包里有道具 2660655（`DNF_MEGA_SPACE=0 DNF_MEGA_POS=66`）
- 组队兼容 `party_compat.json`：补丁 supervisor 打不上补丁时会自动写回 `false`

## 配置与脱敏

仓库里**没有任何真实地址与口令**：

- 启动器默认 IP、SSH 密码、管理密码都是空字符串，首次运行在界面里填
- Python 脚本用环境变量兜底：`DNF_SSH_HOST`、`DNF_SSH_PWD`、`DNF_HOST_IP`、`DNF_CLIENT_DIR`
- `config/启动器配置.example.ini` 是脱敏样例，复制成 `启动器配置.ini` 再填自己的值
- `.gitignore` 已排除个人密钥、`*.ini`、日志、抓包与编译产物

假人管理面板的默认账号是上游的 `admin / twadmin`，**部署后请立刻改掉**。

## 已知限制

- **假人行走动画**：`SET_USER_POSITION` 能让服务端接受坐标并广播，但这台 85A 客户端只把它
  当坐标校正，不产生连续行走动画。真实客户端走的是逐字节异或+循环位移的整段密文批处理，
  与机器人侧的分组加密不是同一条通道。
- **个人商店**：服务端能接受 `CREATE_PRIVATE_STORE` 并返回成功，但客户端不渲染空商店，
  商品上架流程也依赖同一套加密通道。目前可见的摊位是分解机（`CMD 234`）。
- 拍卖行补货直接写库，服务进程只在启动时建搜索索引，所以新货要「重启拍卖」才搜得到。

## 第三方组件与授权

### 假人系统：zmbksyyr/dnf-robot-s4a21

- 上游地址：<https://github.com/zmbksyyr/dnf-robot-s4a21>
- **本仓库不包含、也不分发该项目的源码或二进制**，需要时请自行从上游获取。
- 未包含的原因：上游仓库未声明授权许可证（GitHub API 返回 `license: null`，根目录亦无 LICENSE 文件）。
  在作者明确授权之前转发其源码存在授权风险。
- 本仓库只做两件事：① 在 README 首行给出上游主页与获取方式；② 在 `docs/假人系统改造记录.txt`
  记录我们针对 85A 服务端的适配经验（思路与字段级结论），供读者自行在上游代码上复刻。
- 上游作者若希望收录改造后的分支，欢迎在本仓库提 issue 沟通。

### 其他

- 协议字段对照来自对公开 DOF 服务端源码的阅读与实机抓包验证，仓库内不包含服务端代码。
- 不包含任何游戏客户端资源、PVF 文件、贴图、音频或破解补丁。
- 启动器与脚本依赖的 .NET Framework、Python、paramiko 等均为各自作者的作品。

## 免责声明

- DNF（地下城与勇士）及其素材版权归 Nexon / Neople / 腾讯所有。本仓库只包含自行编写的
  工具代码与协议笔记，**不包含**游戏客户端、服务端二进制、PVF 资源或任何破解补丁。
- 本项目仅供个人学习与局域网联机研究，请勿用于商业用途或公共服务器运营。
- 使用本项目产生的任何后果由使用者自行承担。

## 许可证

本仓库自有代码以 [MIT](LICENSE) 发布。
假人系统是上游项目，**不包含在本仓库内**，其授权以上游仓库为准，见上文「第三方组件与授权」。

## 致谢

- 假人系统：[zmbksyyr/dnf-robot-s4a21](https://github.com/zmbksyyr/dnf-robot-s4a21)
- 协议字段对照参考了公开的 DOF 服务端源码阅读经验
