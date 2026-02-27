---
name: huggingface-manager
description: 用于管理 Hugging Face Spaces 的部署、日志查看及保活配置。支持流式日志获取、Space 状态查询以及通过 keep-alive-24h 服务进行自动保活。
---

# Hugging Face 管理技能

## 目标
协助用户高效管理 Hugging Face Spaces，包括自动化部署、实时日志监控以及确保服务 24 小时在线的保活配置。

## 常用指令

### 1. 基础管理 (使用 manage_spaces.py)
所有指令需确保环境变量 `HF_TOKEN` 已设置。

- **列出所有 Spaces**:
  ```powershell
  python f:\Desktop\kaifa\huggingface-server-skill\scripts\manage_spaces.py list
  ```
- **查看特定 Space 详情**:
  ```powershell
  python f:\Desktop\kaifa\huggingface-server-skill\scripts\manage_spaces.py list [Space名称]
  ```
- **查看运行日志 (SSE 流式)**:
  ```powershell
  python f:\Desktop\kaifa\huggingface-server-skill\scripts\manage_spaces.py logs [Space名称]
  ```
- **查看构建日志 (SSE 流式)**:
  ```powershell
  python f:\Desktop\kaifa\huggingface-server-skill\scripts\manage_spaces.py logs [Space名称] --build
  ```

### 2. Space 保活配置 (Keep-Alive)
Hugging Face 免费层 Space 在闲置 48 小时后会自动休眠。通过 `mingyang22/keep-alive-24h` 服务可以实现保活。

**保活步骤（严禁通过浏览器模拟，必须使用 Git/CLI 操作）：**

1. **下载配置文件**:
   ```powershell
   $env:HF_TOKEN="您的TOKEN"; huggingface-cli download mingyang22/keep-alive-24h index.js --repo-type space --local-dir . --local-dir-use-symlinks False
   ```
2. **修改 `index.js`**:
   在 `webpages` 数组中添加需要保活的 Space URL。
   ```javascript
   const webpages = [
     // ... 现有 URL
     'https://[您的SPACE名称].hf.space', 
   ];
   ```
3. **上传更新**:
   ```powershell
   $env:HF_TOKEN="您的TOKEN"; huggingface-cli upload mingyang22/keep-alive-24h index.js index.js --repo-type=space
   ```

## 部署规范
- **Dockerfile**: 必须包含 `EXPOSE 7860` 且设置 `ENV PORT=7860`。
- **README.md**: 必须包含 YAML 元数据头（包含 `sdk: docker` 及 `app_port: 7860`）。
- **环境变量**: 敏感信息（如 API Key, Cookie）应通过 Space 设置中的 `Secrets` 管理，而非硬编码。

## 故障排除
- 如果日志连接返回 404，请确认使用的是 `logs/run` (对应指令 `logs`) 还是 `logs/build` (对应指令 `logs --build`)。
- 如果推送代码失败，请检查 `HF_TOKEN` 是否具备 `Write` 权限。
