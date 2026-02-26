import gradio as gr
import os
import json
import sqlite3
import pandas as pd
from huggingface_hub import HfApi, hf_hub_download
from datetime import datetime
import shutil
from pathlib import Path
from fastapi.responses import Response

# --- 配置 (优先从环境变量读取) ---
DATASET_REPO_ID = os.environ.get("DATASET_REPO_ID", "mingyang22/huggingface-notes")
HF_TOKEN = os.environ.get("HF_TOKEN") # 必须在 Space 设置中配置
REMOTE_NOTES_PATH = "db/notes.json"

APP_MANIFEST = {
    "name": "HF 笔记 Pro",
    "short_name": "HF笔记",
    "start_url": "/",
    "display": "standalone",
    "background_color": "#0a0a0a",
    "theme_color": "#3b82f6",
    "description": "Hugging Face 高级云端同步笔记",
    "icons": [
        {
            "src": "/pwa-icon.svg",
            "sizes": "any",
            "type": "image/svg+xml",
            "purpose": "any maskable"
        }
    ],
}

PWA_ICON_SVG = """<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 128 128'>
<rect width='128' height='128' rx='28' fill='#0a0a0a' stroke='#333' stroke-width='2'/>
<circle cx='64' cy='64' r='40' fill='url(#grad)'/>
<defs>
<linearGradient id='grad' x1='0' y1='0' x2='1' y2='1'>
<stop offset='0%' stop-color='#7c3aed'/><stop offset='100%' stop-color='#db2777'/>
</linearGradient>
</defs>
<path d='M45 45h38v38H45z' fill='#fff' opacity='0.9'/>
</svg>"""

PWA_HEAD = """
<link rel="manifest" href="/manifest.webmanifest" />
<meta name="theme-color" content="#3b82f6" />
<style>
/* 玻璃质感与高级深色主题 CSS */
:root {
    --bg-dark: #0a0a0a;
    --accent: #3b82f6;
    --border: #333333;
    --text-main: #eeeeee;
    --text-dim: #888888;
}

body, .gradio-container {
    background-color: var(--bg-dark) !important;
    color: var(--text-main) !important;
}

/* 隐藏 Gradio 默认页脚 */
footer { display: none !important; }

/* 侧边栏按钮美化 */
.nav-btn button {
    background: transparent !important;
    border: none !important;
    text-align: left !important;
    padding-left: 20px !important;
    font-size: 16px !important;
    color: var(--text-dim) !important;
}
.nav-btn button:hover {
    background: #1a1a1a !important;
    color: white !important;
}
.active-nav button {
    background: #252525 !important;
    color: white !important;
    border-left: 3px solid var(--accent) !important;
}

/* 列表美化 */
.note-list-item {
    border-bottom: 1px solid #1a1a1a !important;
    padding: 15px !important;
    cursor: pointer;
}
.note-list-item:hover {
    background: #1a1a1a !important;
}

/* 编辑器美化 */
#note_title textarea {
    font-size: 24px !important;
    font-weight: bold !important;
    background: transparent !important;
    border: none !important;
    color: #e0e0e0 !important;
}
#note_content textarea {
    font-size: 16px !important;
    background: transparent !important;
    border: none !important;
    color: #cccccc !important;
}

/* AI 按钮渐变 */
#ai_btn {
    background: linear-gradient(135deg, #7c3aed 0%, #db2777 100%) !important;
    border: none !important;
    font-weight: bold !important;
}

/* 滚动条美化 */
::-webkit-scrollbar { width: 6px; }
::-webkit-scrollbar-track { background: transparent; }
::-webkit-scrollbar-thumb { background: #333; border-radius: 3px; }
::-webkit-scrollbar-thumb:hover { background: #444; }
</style>
"""

def get_default_data_dir():
    # 如果在 Space 环境，优先使用当前目录下的 cache_data 文件夹，避免 /root 权限问题
    if os.environ.get("SPACE_ID") or os.environ.get("HF_SPACE"):
        return str(Path.cwd() / "cache_data")
        
    custom_dir = os.environ.get("HF_NOTES_DATA_DIR")
    if custom_dir: return custom_dir
    if os.name == "nt":
        base = os.environ.get("LOCALAPPDATA") or str(Path.home() / "AppData" / "Local")
    else:
        base = os.environ.get("XDG_DATA_HOME") or str(Path.home() / ".local" / "share")
    return str(Path(base) / "hf-note-app-pro")

DATA_DIR = get_default_data_dir()
LOCAL_NOTES_PATH = str(Path(DATA_DIR) / "notes.json")

def ensure_local_notes():
    Path(DATA_DIR).mkdir(parents=True, exist_ok=True)
    p = Path(LOCAL_NOTES_PATH)
    if not p.exists():
        p.write_text("[]", encoding="utf-8")

def read_notes():
    ensure_local_notes()
    try:
        data = json.loads(Path(LOCAL_NOTES_PATH).read_text(encoding="utf-8-sig"))
        if isinstance(data, list):
            valid_notes = []
            for item in data:
                if not isinstance(item, dict): continue
                # 关键修复：同时支持 C# 风格 (Uppercase) 和 Python 风格 (Lowercase) 的键名
                n_id = item.get("Id") or item.get("id", "")
                n_title = item.get("Title") or item.get("title", "")
                n_content = item.get("Content") or item.get("content", "")
                n_updated = item.get("UpdatedAt") or item.get("updated_at", "")
                n_pinned = item.get("IsPinned") if "IsPinned" in item else item.get("is_pinned", False)
                n_deleted = item.get("IsDeleted") if "IsDeleted" in item else item.get("is_deleted", False)

                valid_notes.append({
                    "id": str(n_id),
                    "title": str(n_title),
                    "content": str(n_content),
                    "updated_at": str(n_updated),
                    "is_pinned": bool(n_pinned),
                    "is_deleted": bool(n_deleted)
                })
            return valid_notes
    except Exception as e:
        print(f"读取笔记失败: {e}")
    return []

def write_notes(notes):
    ensure_local_notes()
    Path(LOCAL_NOTES_PATH).write_text(
        json.dumps(notes, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )

# --- 持久化管理 ---
class CloudSync:
    def __init__(self):
        self.api = HfApi(token=HF_TOKEN)
    
    def pull(self):
        try:
            ensure_local_notes()
            print(f"🔄 正在从 Dataset {DATASET_REPO_ID} 拉取 {REMOTE_NOTES_PATH}...")
            downloaded_path = hf_hub_download(
                repo_id=DATASET_REPO_ID,
                filename=REMOTE_NOTES_PATH,
                repo_type="dataset",
                token=HF_TOKEN,
                force_download=True,
                revision="main",
            )
            shutil.copy(downloaded_path, LOCAL_NOTES_PATH)
            return True, f"✅ 云端拉取同步完成"
        except Exception as e:
            msg = str(e)
            print(f"拉取失败详情: {msg}")
            # 如果是 401/404，通常是 Token 没设或权限问题
            if "401" in msg or "404" in msg:
                return False, f"⚠️ 拉取失败: 请检查 Space 的 HF_TOKEN 是否已正确配置 (Dataset 可能为私有)"
            return False, f"⚠️ 拉取失败: {msg}"

    def push(self):
        ensure_local_notes()
        if not os.path.exists(LOCAL_NOTES_PATH): return False, "❌ 文件丢失"
        try:
            self.api.upload_file(
                path_or_fileobj=LOCAL_NOTES_PATH,
                path_in_repo=REMOTE_NOTES_PATH,
                repo_id=DATASET_REPO_ID,
                repo_type="dataset",
                commit_message=f"Web Update Pro at {datetime.now().strftime('%H:%M:%S')}"
            )
            return True, "✅ 已备份至云端"
        except Exception as e:
            return False, f"❌ 备份失败: {e}"

sync_manager = CloudSync()

# --- 业务逻辑 ---
def load_notes_list(filter_type="all", search_query=""):
    notes = read_notes()
    query = search_query.lower() if search_query else ""
    
    filtered = []
    for n in notes:
        # Tab 过滤
        is_deleted = n.get("is_deleted", False)
        is_pinned = n.get("is_pinned", False)
        
        if filter_type == "trash":
            if not is_deleted: continue
        else:
            if is_deleted: continue
            if filter_type == "pinned" and not is_pinned: continue
            
        # 搜索过滤
        if query and query not in n["title"].lower() and query not in n["content"].lower():
            continue
        
        filtered.append(n)
        
    # 排序：置顶优先，时间倒序
    sorted_notes = sorted(filtered, key=lambda x: (x.get("is_pinned", False), x.get("updated_at", "")), reverse=True)
    
    return [
        [n["id"], f"{'📌 ' if n.get('is_pinned') else ''}{n['title'] or '未命名'}", n["updated_at"]]
        for n in sorted_notes
    ]

def get_note_detail(note_id):
    if not note_id: return "", "", ""
    notes = read_notes()
    for n in notes:
        if n["id"] == note_id:
            return n["title"], n["content"], n["updated_at"]
    return "", "", ""

def handle_save(note_id, title, content):
    if not title and not content: return "无内容可保存", load_notes_list()
    notes = read_notes()
    now = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    
    found = False
    for n in notes:
        if n["id"] == note_id:
            n["title"], n["content"], n["updated_at"] = title, content, now
            found = True
            break
            
    if not found:
        new_id = datetime.now().strftime("%Y%m%d%H%M%S")
        new_note = {
            "id": new_id,
            "title": title or "新笔记",
            "content": content,
            "updated_at": now,
            "is_pinned": False,
            "is_deleted": False
        }
        notes.insert(0, new_note)
        note_id = new_id
        
    write_notes(notes)
    _, msg = sync_manager.push()
    return f"已本地保存 | {msg}", load_notes_list(), note_id

def handle_delete(note_id, current_filter):
    if not note_id: return "未选择笔记", load_notes_list(current_filter), ""
    notes = read_notes()
    for n in notes:
        if n["id"] == note_id:
            if current_filter == "trash":
                notes.remove(n)
            else:
                n["is_deleted"] = True
                n["is_pinned"] = False
            break
    write_notes(notes)
    sync_manager.push()
    return "已移至回收站" if current_filter != "trash" else "已彻底删除", load_notes_list(current_filter), ""

def handle_pin(note_id, current_filter):
    if not note_id: return load_notes_list(current_filter)
    notes = read_notes()
    for n in notes:
        if n["id"] == note_id:
            n["is_pinned"] = not n.get("is_pinned", False)
            break
    write_notes(notes)
    backup_msg = sync_manager.push()[1]
    return load_notes_list(current_filter)

# --- Gradio UI ---
with gr.Blocks(theme=gr.themes.Default(), head=PWA_HEAD) as demo:
    current_filter_state = gr.State("all")
    selected_note_id = gr.State("")
    
    with gr.Row(equal_height=True):
        # 1. 导航栏 (ClassNote 风格)
        with gr.Column(scale=1, min_width=150):
            gr.HTML("<div style='font-size: 20px; font-weight: bold; margin-bottom: 30px; color: white;'>HF Note</div>")
            btn_all = gr.Button("全部笔记", variant="secondary", elem_classes=["nav-btn", "active-nav"])
            btn_pinned = gr.Button("已置顶", variant="secondary", elem_classes=["nav-btn"])
            btn_trash = gr.Button("回收站", variant="secondary", elem_classes=["nav-btn"])
            
            gr.Markdown("---")
            btn_new = gr.Button("➕ 新建笔记", variant="secondary")
            btn_sync_pull = gr.Button("🔄 同步云端", variant="secondary")
            
        # 2. 列表栏
        with gr.Column(scale=2, min_width=250):
            search_box = gr.Textbox(placeholder="搜索笔记...", show_label=False, elem_id="search_box")
            note_list = gr.Dataframe(
                headers=["ID", "标题", "时间"],
                datatype=["str", "str", "str"],
                col_count=(3, "fixed"),
                interactive=False,
                label=None,
            )
            status_text = gr.Markdown("就绪")
            
        # 3. 编辑器栏
        with gr.Column(scale=4):
            with gr.Row():
                btn_pin = gr.Button("📌 置顶", variant="secondary", size="sm")
                btn_del = gr.Button("🗑️ 删除", variant="stop", size="sm")
                btn_ai = gr.Button("AI 润色", variant="primary", size="sm", elem_id="ai_btn")
            
            edit_title = gr.Textbox(placeholder="无标题笔记", show_label=False, elem_id="note_title")
            edit_content = gr.TextArea(placeholder="暂无内容，开始输入...", show_label=False, lines=25, elem_id="note_content")
            edit_date = gr.Markdown("", elem_id="note_date")

    # --- 交互事件 ---
    
    def on_note_select(evt: gr.SelectData, filt):
        curr_list = load_notes_list(filt)
        note_id = curr_list[evt.index[0]][0]
        title, content, date = get_note_detail(note_id)
        return note_id, title, content, f"最后修改: {date}"

    note_list.select(on_note_select, [current_filter_state], [selected_note_id, edit_title, edit_content, edit_date])
    
    search_box.change(load_notes_list, [current_filter_state, search_box], [note_list])
    
    # 离开焦点时保存
    edit_title.blur(handle_save, [selected_note_id, edit_title, edit_content], [status_text, note_list, selected_note_id])
    edit_content.blur(handle_save, [selected_note_id, edit_title, edit_content], [status_text, note_list, selected_note_id])
    
    btn_new.click(lambda: ("", "新笔记", "", ""), None, [selected_note_id, edit_title, edit_content, edit_date])
    btn_pin.click(handle_pin, [selected_note_id, current_filter_state], [note_list])
    btn_del.click(handle_delete, [selected_note_id, current_filter_state], [status_text, note_list, selected_note_id])
    
    btn_sync_pull.click(lambda: (sync_manager.pull()[1], load_notes_list()), None, [status_text, note_list])
    
    def ai_polish(content):
        if not content: return content
        return f"✨ [AI 润色已模拟完成]\n\n{content}\n\n(请在本地动作中使用完整的 DeepSeek 润色服务)"
    btn_ai.click(ai_polish, [edit_content], [edit_content])

    # 启动拉取
    demo.load(lambda: (sync_manager.pull()[1], load_notes_list()), None, [status_text, note_list])

if __name__ == "__main__":
    demo.launch()
