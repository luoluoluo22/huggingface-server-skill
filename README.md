# HuggingFace Server Skill (Official SDK Management)

基于官方 `huggingface-hub` SDK 构建的自动化运维工具箱。

## 🛠️ 管理工具 (Scripts)

| 脚本路径                         | 功能说明                                                                               |
| :------------------------------- | :------------------------------------------------------------------------------------- |
| `scripts/manage_spaces.py`       | **Space 管家**：管理服务生命周期（重启、暂停、日志、硬件、变量设置等）。               |
| `scripts/manage_datasets.py`     | **数据管家**：管理云端数据库 (Dataset) 资产（列表展示、文件扫描、新建/删除库）。       |
| `scripts/persistence_manager.py` | **持久化 SDK**：提供 `restore()` 和 `save()` 方法，方便在任何 App 中集成云端同步备份。 |

## 🕹️ 演示与示例 (Demos)

- `demo_persistence.py`: 本地读档/存档操作演示。
- `hf-note-app/`: **Quicker 笔记同步客户端**源码。
  - `quicker/`: Quicker 动作定义文件 (.json, .cs)。
  - `scripts/`: 笔记数据库与同步逻辑脚本。
- `app.py`: 部署在 HF Space 的持久化 Gradio 网页应用示例。

## 📖 技术文档 (Docs)

- [**Hugging Face 作为后端/数据库的可行性分析**](./docs/huggingface_as_backend_analysis.md)
- [**详细操作指南 (SKILL.md)**](./SKILL.md)

---

## 🚀 快速上手

1. **环境准备**: `pip install -r requirements.txt`
2. **设置 Token**: `export HF_TOKEN="your_token_here"` 或在 Windows 中设置环境变量。
3. **列出所有服务**: `python scripts/manage_spaces.py list`
4. **列出所有数据库**: `python scripts/manage_datasets.py dataset --list`

---

## 📂 项目结构
```text
huggingface-server-skill/
├── docs/                   # 深度技术文档
├── scripts/                # 核心管理脚本与 SDK
├── SKILL.md                # 技能描述与用法指南
├── requirements.txt        # 依赖清单
└── demo_persistence.py     # 快速上手演示
```
