import os
import time
from scripts.persistence_manager import PersistenceManager

# --- 配置区 ---
# 建议在 Space 的 Settings -> Variables 中设置这两个环境变量
DATASET_ID = os.environ.get("DATASET_REPO_ID", "luoluoluo22/my-space-storage")
DB_FILE = "counter.txt"
REMOTE_PATH = "backups/counter.txt"

def main():
    print("🚀 启动持久化 Demo 服务...")
    
    # 1. 初始化持久化管理器
    try:
        pm = PersistenceManager(dataset_id=DATASET_ID)
    except Exception as e:
        print(f"❌ 初始化失败: {e}")
        # 如果是本地测试且没设环境变量，可以手动传参: pm = PersistenceManager("username/repo")
        return

    # 2. 【启动阶段】尝试恢复数据 (读档)
    print("--- 步骤 1: 尝试从云端恢复数据 ---")
    pm.restore(REMOTE_PATH, DB_FILE)

    # 读取当前数值
    count = 0
    if os.path.exists(DB_FILE):
        with open(DB_FILE, "r") as f:
            try:
                count = int(f.read().strip())
                print(f"📈 恢复成功！当前计数为: {count}")
            except:
                print("⚠️ 文件内容损坏，从 0 开始。")
    else:
        print("ℹ️ 未发现云端备份，从 0 开始。")

    # 3. 【业务执行阶段】逻辑处理
    count += 1
    print(f"正在处理业务逻辑... 计数更新为: {count}")
    
    # 模拟数据写入本地磁盘
    with open(DB_FILE, "w") as f:
        f.write(str(count))

    # 4. 【保存阶段】将新数据备份回云端 (存档)
    print("--- 步骤 2: 将更新后的数据同步至云端 ---")
    pm.save(DB_FILE, REMOTE_PATH, commit_message=f"Update counter to {count} from Space")

    print(f"✅ 执行完毕！重启 Space 后，计数将从 {count} 继续累加。")

if __name__ == "__main__":
    main()
