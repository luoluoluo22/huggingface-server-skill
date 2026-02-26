import os
import shutil
from pathlib import Path
from huggingface_hub import HfApi, hf_hub_download, upload_folder, upload_file

class PersistenceManager:
    """
    HuggingFace Space 持久化层：利用私有 Dataset 进行数据备份与恢复。
    适用于免费 Tier 的 Space 避免重启后数据丢失。
    """
    def __init__(self, dataset_id=None, token=None):
        self.api = HfApi(token=token or os.environ.get("HF_TOKEN"))
        self.dataset_id = dataset_id or os.environ.get("DATASET_REPO_ID")
        
        if not self.dataset_id:
            raise ValueError("未指定 DATASET_REPO_ID，请在初始化或环境变量中提供。格式：'username/dataset-name'")

    def restore(self, remote_path, local_path, is_folder=False):
        """
        从 Dataset 恢复数据到本地。
        :param remote_path: Dataset 里的路径（如 'db/data.sqlite'）
        :param local_path: 本地存放的路径
        :param is_folder: 是否是文件夹。如果是文件夹，会尝试下载整个目录内容。
        """
        print(f"正在从 [{self.dataset_id}] 恢复数据: {remote_path} -> {local_path}...")
        try:
            if is_folder:
                # 确保本地目录存在
                os.makedirs(local_path, exist_ok=True)
                # 这里的逻辑稍复杂，官方 SDK 并没有直接对应 restore_folder，
                # 通常是下载整个 Repo 或逐个获取。
                # 简化处理：要求用户明确指定关键文件或使用 snapshot_download。
                from huggingface_hub import snapshot_download
                snapshot_download(
                    repo_id=self.dataset_id,
                    repo_type="dataset",
                    local_dir=local_path,
                    allow_patterns=f"{remote_path}/*",
                    token=self.api.token,
                    revision="main"
                )
            else:
                # 下载单文件
                downloaded_path = hf_hub_download(
                    repo_id=self.dataset_id,
                    repo_type="dataset",
                    filename=remote_path,
                    token=self.api.token,
                    force_download=True,
                    revision="main"
                )
                # 移动到目标位置
                target = Path(local_path)
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy(downloaded_path, local_path)
            
            print("✅ 恢复完成。")
        except Exception as e:
            print(f"⚠️ 恢复失败（可能是文件尚不存在）: {e}")

    def save(self, local_path, remote_path, is_folder=False, commit_message="Persist data from Space"):
        """
        将本地数据备份到 Dataset。
        :param local_path: 本地待备份的路径
        :param remote_path: Dataset 里的保存路径
        :param is_folder: 是否是整个文件夹
        """
        print(f"正在备份到 [{self.dataset_id}]: {local_path} -> {remote_path}...")
        try:
            if is_folder:
                self.api.upload_folder(
                    folder_path=local_path,
                    path_in_repo=remote_path,
                    repo_id=self.dataset_id,
                    repo_type="dataset",
                    commit_message=commit_message
                )
            else:
                self.api.upload_file(
                    path_or_fileobj=local_path,
                    path_in_repo=remote_path,
                    repo_id=self.dataset_id,
                    repo_type="dataset",
                    commit_message=commit_message
                )
            print("✅ 备份成功。")
        except Exception as e:
            print(f"❌ 备份失败: {e}")

if __name__ == "__main__":
    # 简单的 CLI 测试示例
    print("Persistence Manager CLI...")
    # 用法示例：python persistence_manager.py save data.db db/data.db
    import sys
    if len(sys.argv) < 4:
        print("用法: python persistence_manager.py [save|restore] <local_path> <remote_path> [--folder]")
        sys.exit(0)
    
    op = sys.argv[1]
    lp = sys.argv[2]
    rp = sys.argv[3]
    is_f = "--folder" in sys.argv
    
    mgr = PersistenceManager() # 会自动读取环境变量
    if op == "save":
        mgr.save(lp, rp, is_folder=is_f)
    elif op == "restore":
        mgr.restore(rp, lp, is_folder=is_f)
