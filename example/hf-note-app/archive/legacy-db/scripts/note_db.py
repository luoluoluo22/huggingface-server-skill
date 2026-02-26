import sqlite3
import os

DB_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "data", "notes.db")
os.makedirs(os.path.dirname(DB_PATH), exist_ok=True)

def get_db():
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    # 自动执行初始化，防止表不存在
    conn.execute("""
        CREATE TABLE IF NOT EXISTS notes (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            title TEXT,
            content TEXT,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
        )
    """)
    return conn

def init_db():
    with get_db() as conn:
        conn.execute("""
            CREATE TABLE IF NOT EXISTS notes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT,
                content TEXT,
                updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
            )
        """)

def save_note(title, content, note_id=None):
    with get_db() as conn:
        if note_id:
            conn.execute("UPDATE notes SET title=?, content=?, updated_at=CURRENT_TIMESTAMP WHERE id=?", (title, content, note_id))
        else:
            conn.execute("INSERT INTO notes (title, content) VALUES (?, ?)", (title, content))

def list_notes():
    with get_db() as conn:
        return [dict(row) for row in conn.execute("SELECT * FROM notes ORDER BY updated_at DESC").fetchall()]

if __name__ == "__main__":
    init_db()
    print("Database initialized at", DB_PATH)
