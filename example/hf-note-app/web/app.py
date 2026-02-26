import gradio as gr
import os
import json
import sqlite3
import pandas as pd
from huggingface_hub import HfApi, hf_hub_download
from datetime import datetime
import shutil
from pathlib import Path

# --- 配置 (优先从环境变量读取) ---
DATASET_REPO_ID = os.environ.get("DATASET_REPO_ID", "mingyang22/huggingface-notes")
HF_TOKEN = os.environ.get("HF_TOKEN") # 必须在 Space 设置中配置
LOCAL_NOTES_PATH = "./notes.json"
LEGACY_DB_PATH = "./notes.db"
REMOTE_NOTES_PATH = "db/notes.json"

# --- JSON 存储工具 ---
def ensure_local_notes():
    p = Path(LOCAL_NOTES_PATH)
    if not p.exists():
        migrate_from_legacy_db()
        if not p.exists():
            p.write_text("[]", encoding="utf-8")

def migrate_from_legacy_db():
    if not os.path.exists(LEGACY_DB_PATH):
        return
    try:
        conn = sqlite3.connect(LEGACY_DB_PATH, check_same_thread=False)
        conn.row_factory = sqlite3.Row
        rows = conn.execute(
            "SELECT id, title, content, updated_at FROM notes ORDER BY updated_at DESC"
        ).fetchall()
        conn.close()
        notes = [
            {
                "id": int(r["id"]),
                "title": str(r["title"] or ""),
                "content": str(r["content"] or ""),
                "updated_at": str(r["updated_at"] or ""),
            }
            for r in rows
        ]
        write_notes(notes)
        print(f"✅ 已从旧版 notes.db 迁移 {len(notes)} 条记录到 notes.json")
    except Exception as e:
        print(f"⚠️ 旧版 notes.db 迁移失败: {e}")

def read_notes():
    ensure_local_notes()
    try:
        # Use utf-8-sig to tolerate BOM-prefixed JSON from external clients.
        data = json.loads(Path(LOCAL_NOTES_PATH).read_text(encoding="utf-8-sig"))
        if isinstance(data, list):
            notes = []
            for item in data:
                if not isinstance(item, dict):
                    continue
                notes.append(
                    {
                        "id": int(item.get("id", 0)),
                        "title": str(item.get("title", "")),
                        "content": str(item.get("content", "")),
                        "updated_at": str(item.get("updated_at", "")),
                    }
                )
            return notes
    except Exception:
        pass
    return []

def write_notes(notes):
    Path(LOCAL_NOTES_PATH).write_text(
        json.dumps(notes, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )

# --- 持久化管理 (云端同步) ---
class CloudSync:
    def __init__(self):
        self.api = HfApi(token=HF_TOKEN)
    
    def pull(self):
        """从 Dataset 下载最新的 notes.json"""
        print(f"🔄 正在从云端拉取数据: {DATASET_REPO_ID}...")
        try:
            downloaded_path = hf_hub_download(
                repo_id=DATASET_REPO_ID,
                filename=REMOTE_NOTES_PATH,
                repo_type="dataset",
                token=HF_TOKEN,
                force_download=True,  # 同步核心：跳过本地缓存
                revision="main",
            )
            shutil.copy(downloaded_path, LOCAL_NOTES_PATH)
            return f"✅ 数据拉取成功 ({datetime.now().strftime('%H:%M:%S')})"
        except Exception as e:
            print(f"⚠️ 拉取失败: {e}")
            ensure_local_notes()
            return "ℹ️ 云端暂无 notes.json 或拉取失败。"

    def push(self):
        """将本地 notes.json 上传到 Dataset"""
        if not os.path.exists(LOCAL_NOTES_PATH):
            return "❌ 本地 notes.json 丢失"
            
        file_size = os.path.getsize(LOCAL_NOTES_PATH)
        print(f"📤 正在上传数据到云端 (Size: {file_size} bytes): {DATASET_REPO_ID}...")
        try:
            self.api.upload_file(
                path_or_fileobj=LOCAL_NOTES_PATH,
                path_in_repo=REMOTE_NOTES_PATH,
                repo_id=DATASET_REPO_ID,
                repo_type="dataset",
                commit_message=f"Web update Size({file_size}) at {datetime.now().strftime('%H:%M:%S')}"
            )
            return f"✅ 云端备份已更新 ({datetime.now().strftime('%H:%M:%S')})"
        except Exception as e:
            return f"❌ 备份失败: {e}"

sync_manager = CloudSync()

# --- 业务逻辑 ---
def load_notes_list():
    notes = sorted(read_notes(), key=lambda x: x.get("updated_at", ""), reverse=True)
    df = pd.DataFrame(
        [
            {
                "id": str(n.get("id", "")),
                "title": n.get("title", ""),
                "updated_at": n.get("updated_at", ""),
            }
            for n in notes
        ]
    )
    if df.empty:
        return [["(空)", "请创建您的第一条笔记", ""]]
    return df.values.tolist()

def get_note_content(note_id):
    if not note_id or note_id == "(空)":
        return "", ""
    notes = read_notes()
    for n in notes:
        if str(n.get("id")) == str(note_id):
            return n.get("title", ""), n.get("content", "")
    return "", ""

def save_note(note_id, title, content):
    if not title: return "❌ 标题不能为空", load_notes_list()

    notes = read_notes()
    now = datetime.utcnow().strftime("%Y-%m-%d %H:%M:%S")
    if note_id and str(note_id).isdigit():
        target = None
        for n in notes:
            if int(n.get("id", -1)) == int(note_id):
                target = n
                break
        if target:
            target["title"] = title
            target["content"] = content
            target["updated_at"] = now
        else:
            next_id = (max([int(n.get("id", 0)) for n in notes]) + 1) if notes else 1
            notes.append({"id": next_id, "title": title, "content": content, "updated_at": now})
        msg = "📝 笔记已更新 (本地)"
    else:
        next_id = (max([int(n.get("id", 0)) for n in notes]) + 1) if notes else 1
        notes.append({"id": next_id, "title": title, "content": content, "updated_at": now})
        msg = "✨ 笔记已创建 (本地)"
    write_notes(notes)
    
    # 自动触发云端同步备份
    backup_msg = sync_manager.push()
    return f"{msg} | {backup_msg}", load_notes_list()

def delete_note(note_id):
    if not note_id: return "请选择笔记", load_notes_list()
    notes = [n for n in read_notes() if str(n.get("id")) != str(note_id)]
    write_notes(notes)
    backup_msg = sync_manager.push()
    return f"🗑️ 笔记已删除 | {backup_msg}", load_notes_list()

# --- Gradio UI 界面 ---
with gr.Blocks(theme=gr.themes.Soft(primary_hue="blue")) as demo:
    gr.Markdown("# 📓 Hugging Face 个人笔记云端版")
    gr.Markdown("实时同步本地 Quicker 动作数据。数据由私有 Dataset 承载，安全、持久、版本可追溯。")
    
    with gr.Row():
        with gr.Column(scale=1):
            note_list = gr.Dataframe(
                headers=["ID", "标题", "最后修改"],
                datatype=["str", "str", "str"],
                value=load_notes_list(),
                interactive=False,
                label="我的笔记列表"
            )
            btn_refresh = gr.Button("🔄 刷新并手动拉取云端", variant="secondary")
            status_output = gr.Markdown("系统就绪")
            
        with gr.Column(scale=2):
            with gr.Group():
                target_id = gr.Textbox(visible=False)
                in_title = gr.Textbox(label="标题", placeholder="输入笔记标题...")
                in_content = gr.TextArea(label="正文内容", lines=15, placeholder="记录您的想法...")
                
                with gr.Row():
                    btn_save = gr.Button("💾 保存并推送到云端", variant="primary")
                    btn_new = gr.Button("➕ 新建笔记")
                    btn_del = gr.Button("🗑️ 删除笔记", variant="stop")

    # 事件绑定
    def on_select(evt: gr.SelectData):
        # evt.index[0] 是行号
        df = load_notes_list()
        selected_id = df[evt.index[0]][0]
        title, content = get_note_content(selected_id)
        return selected_id, title, content

    note_list.select(on_select, None, [target_id, in_title, in_content])
    
    btn_save.click(save_note, [target_id, in_title, in_content], [status_output, note_list])
    
    btn_new.click(lambda: (None, "新笔记", ""), None, [target_id, in_title, in_content])
    
    btn_del.click(delete_note, [target_id], [status_output, note_list])
    
    btn_refresh.click(lambda: (sync_manager.pull(), load_notes_list()), None, [status_output, note_list])

    # 启动时自动从云端拉取
    demo.load(lambda: (sync_manager.pull(), load_notes_list()), None, [status_output, note_list])

if __name__ == "__main__":
    demo.launch()
