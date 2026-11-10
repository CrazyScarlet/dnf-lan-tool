# 贡献指南

1. Fork 本仓库，从 `main` 开分支。
2. 提交前确认没有把以下内容带进 diff：
   - 真实 IP、域名、SSH / MySQL / 管理面板口令
   - `*.pem`、`*.key`、`*.ini`（示例文件用 `.example.ini`）
   - 日志、抓包、编译产物
3. 代码风格：
   - C#：保持单文件 WinForms，不引入第三方 NuGet 依赖
   - Python：兼容 3.8+，只用标准库 + `paramiko`
   - Go：`gofmt`，不新增依赖
4. PR 描述里写清楚「改了什么、怎么验证的」，涉及协议的请附字段说明。
