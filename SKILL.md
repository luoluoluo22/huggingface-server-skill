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
- **Space 生命周期管理**：使用 `scripts/manage_spaces.py`
  - 列出 Spaces：`python scripts/manage_spaces.py list`
  - 重启/暂停/唤醒：`python scripts/manage_spaces.py action <name> [restart|pause|wakeup]`
  - 配置 Secrets/变量：`python scripts/manage_spaces.py config <name> [--get|--key|--val]`
  - 查看日志：`python scripts/manage_spaces.py logs <name>`
  - 硬件切换：`python scripts/manage_spaces.py hardware <name> [--set t4-small]`
- **云端数据库 (Dataset) 管理**：使用 `scripts/manage_datasets.py`
  - 列出数据库：`python scripts/manage_datasets.py list`
  - 扫描备份内容：`python scripts/manage_datasets.py view <dataset_name>`
  - 新建/删除：`python scripts/manage_datasets.py [create|delete] <name>`
- **数据持久化 SDK (Persistence Layer)**：
  - **原理**：利用私有 HF Dataset 作为后端存储，解决 Space 重启丢失数据的问题。
  - **组件**：`scripts/persistence_manager.py` (内含 `PersistenceManager` 类)
  - **用法**：在应用启动时调用 `pm.restore()`，数据变更时调用 `pm.save()`。
- 在调用任何 API 时，优先检查是否存在 `HF_TOKEN` 环境变量。

## Constraints
- 绝不在最终输出和日志中明文打印任何私密的授权 token (`HF_TOKEN`)。
- 如果请求返回的内容超过 50 行，适度总结结果，不要直接将整篇长 JSON 输出给用户。
