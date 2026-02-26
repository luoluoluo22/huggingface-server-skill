import sys
import os
import io

# 确保能找到 persistence_manager 和 note_db
BASE_DIR = os.path.dirname(os.path.abspath(__file__)) # .../scripts
PROJECT_ROOT = os.path.dirname(BASE_DIR) # .../hf-note-app
SKILL_ROOT = os.path.join(os.path.dirname(PROJECT_ROOT), "huggingface-server-skill")
LOG_FILE = os.path.join(PROJECT_ROOT, "data", "sync.log")

class DualLogger:
    def __init__(self, filename):
        # 兼容 GBK 终端，但写入文件为 UTF-8
        self.terminal = sys.stdout
        self.log = open(filename, "w", encoding="utf-8")
    def write(self, message):
        try:
            if hasattr(self.terminal, 'buffer'):
                self.terminal.buffer.write(message.encode('utf-8', 'replace'))
            else:
                self.terminal.write(message)
        except:
            pass
        self.log.write(message)
        self.log.flush()
    def flush(self):
        try:
            self.terminal.flush()
        except:
            pass
        self.log.flush()

sys.stdout = DualLogger(LOG_FILE)
sys.stderr = sys.stdout

sys.path.append(BASE_DIR)
sys.path.append(os.path.join(SKILL_ROOT, "scripts"))

from note_db import save_note, list_notes, init_db
from persistence_manager import PersistenceManager

# --- 配置 ---
DATASET_ID = "mingyang22/huggingface-notes" # 使用已创建的库
HF_TOKEN = os.environ.get("HF_TOKEN")
LOCAL_DB = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "data", "notes.db")
REMOTE_DB = "db/notes.db"

if not HF_TOKEN:
    raise RuntimeError("未检测到 HF_TOKEN 环境变量，请先设置后再执行同步。")

def sync():
    pm = PersistenceManager(dataset_id=DATASET_ID, token=HF_TOKEN)
    
    print(f"[*] 开始云端拉取 (Dataset: {DATASET_ID}, Branch: main)...")
    # 1. 启动时拉取最新。先拉取，再 init_db 防止文件占用。
    pm.restore(REMOTE_DB, LOCAL_DB)
    
    # 2. 拉取完成后初始化表格（如果库是新的）
    init_db()
    
    if os.path.exists(LOCAL_DB):
        import time
        mtime = time.ctime(os.path.getmtime(LOCAL_DB))
        print(f"[OK] 恢复完成。本地 DB 状态: 已更新 ({mtime})")
    else:
        print("[Error] 恢复失败：本地文件不存在。")

def push_to_cloud():
    pm = PersistenceManager(dataset_id=DATASET_ID, token=HF_TOKEN)
    print("[*] 正在上传备份到 Hugging Face...")
    pm.save(LOCAL_DB, REMOTE_DB)
    print("[OK] 备份完成。")

if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "push":
        push_to_cloud()
    else:
        sync()
