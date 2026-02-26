---
name: huggingface-server-skill
description: 这是一个用于与 HuggingFace 服务器进行交互和管理的技能。当用户需要测试与HuggingFace的连接、管理模型/空间，或者调用HuggingFace API时触发此技能。
---

# HuggingFace Server Skill

## Goal
管理、测试或部署 HuggingFace 相关的服务和内容，以及测试与 HuggingFace 平台的连接状态。

## Authentication (认证连接)
要与 HuggingFace 建立连接，需要提供有效的 Access Token (`HF_TOKEN`)。
- 建议用户将 `HF_TOKEN` 配置在系统的环境变量中。
- 所有需要访问个人账户、私有模型或创建修改资源的请求，均需附带此 Token 进行 Bearer 认证。
- 如果请求需要连接特定的 Endpoint URL (例如 Inference Endpoints)，需要同时确保使用相应的 URL 请求地址。

## Instructions
- 根据用户的自然语言请求意图，分析所需的 HuggingFace 交互意图（如：测试连接、下载模型、查询账户信息）。
- 当用户要求测试或建立连接时，执行 `scripts/test_hf_connection.py` 脚本来验证 `HF_TOKEN` 的有效性以及网络连通性。
  - 命令示例：`python scripts/test_hf_connection.py` 
- **Space 管理与交互**：执行 `scripts/manage_spaces.py`可进行详尽的后台管理。
  - **列出 Spaces**：`python scripts/manage_spaces.py list` （包含运行状态与App直连URL，可作为BaseURL）
  - **生命周期控制**：`python scripts/manage_spaces.py action <space_name> [restart|pause|wakeup]` （比如遇到 Error 或修改密码后，进行 restart 重启使之生效。Pause休眠可节约资源。）
  - **新建 Space**：`python scripts/manage_spaces.py create <新建的名字> --sdk docker` (通过代码静默创建一个全新的私有或公开的 Space)
  - **私密环境变量/Secrets读写**：
    - 读取现存键名：`python scripts/manage_spaces.py secrets <space_name> --get` （只会取回名，不会暴露值）
    - 写入变量配置：`python scripts/manage_spaces.py secrets <space_name> --set_key OPENAI_API_KEY --set_value sk-xxxxx` （如代理应用的配置源等）
  - **获取日志**：`python scripts/manage_spaces.py logs <space_name>` （由于官方 SDK 已集成，现在支持更全的日志获取）
  - **硬件规格管理**：
    - 查询当前规格：`python scripts/manage_spaces.py hardware <space_name>`
    - 切换硬件规格：`python scripts/manage_spaces.py hardware <space_name> --set t4-small` （用于高性能推理任务切换）
  - **数据持久化系统 (Persistence Layer)**：
    - **原理**：利用私有 HF Dataset 作为后端存储，解决 Space 重启丢失数据的问题。
    - **基本用法**（在 Space 启动代码中使用）：
      ```python
      from scripts.persistence_manager import PersistenceManager
      pm = PersistenceManager("your-username/private-dataset-id")
      # 1. 启动时恢复数据
      pm.restore("db/data.sqlite", "./local_data.db")
      # 2. 变更时保存备份
      pm.save("./local_data.db", "db/data.sqlite")
      ```
    - **手工操作**：`python scripts/persistence_manager.py save <本地文件> <云端路径>`
- 在调用任何 API 时，优先检查是否存在 `HF_TOKEN` 环境变量。

## Constraints
- 绝不在最终输出和日志中明文打印任何私密的授权 token (`HF_TOKEN`)。
- 如果请求返回的内容超过 50 行，适度总结结果，不要直接将整篇长 JSON 输出给用户。
