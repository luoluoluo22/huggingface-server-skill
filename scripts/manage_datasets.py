import os
import sys
import argparse
from huggingface_hub import HfApi

def get_api():
    hf_token = os.environ.get("HF_TOKEN")
    if not hf_token:
        print("错误: 未找到系统环境变量 'HF_TOKEN'。")
        sys.exit(1)
    return HfApi(token=hf_token)

def get_username(api):
    try:
        user_info = api.whoami()
        return user_info.get("name")
    except Exception as e:
        print(f"获取账户信息失败: {e}")
        sys.exit(1)

def list_datasets():
    api = get_api()
    username = get_username(api)
    print(f"正在拉取 {username} 的 Datasets (云端数据库) 列表...\n")
    try:
        datasets = api.list_datasets(author=username)
        print(f"{'Dataset 名称':<30} | {'私有':<5} | {'最后更新':<25}")
        print("-" * 75)
        count = 0
        for ds in datasets:
            name = ds.id.split("/")[-1]
            is_private = "Yes" if ds.private else "No"
            last_modified = getattr(ds, 'lastModified', 'N/A')
            print(f"{name:<30} | {is_private:<5} | {last_modified}")
            count += 1
        print("-" * 75)
        print(f"共发现 {count} 个数据库资产。\n")
    except Exception as e:
        print(f"获取 Datasets 列表失败: {e}")

def view_dataset(dataset_id):
    api = get_api()
    if "/" not in dataset_id:
        username = get_username(api)
        repo_id = f"{username}/{dataset_id}"
    else:
        repo_id = dataset_id
        
    print(f"正在扫描数据库 [{repo_id}] 存档的文件内容...\n")
    try:
        files = api.list_repo_files(repo_id=repo_id, repo_type="dataset")
        if not files:
            print("该数据库目前为空。")
        else:
            for f in files:
                if f.startswith(".") or f == ".gitattributes": continue
                print(f" 📂 {f}")
        print("\n扫描完成。")
    except Exception as e:
        print(f"查看失败: {e}")

def create_dataset(name, is_private=True):
    api = get_api()
    print(f"正在创建新的云端数据库: {name} (私密: {is_private})...")
    try:
        repo_url = api.create_repo(
            repo_id=name,
            repo_type="dataset",
            private=is_private,
            exist_ok=True
        )
        print(f"✅ 数据库创建成功！")
        print(f"➜ 链接: {repo_url}")
    except Exception as e:
        print(f"❌ 创建失败: {e}")

def delete_dataset(dataset_id):
    api = get_api()
    if "/" not in dataset_id:
        username = get_username(api)
        repo_id = f"{username}/{dataset_id}"
    else:
        repo_id = dataset_id
        
    confirm = input(f"⚠️ 确定要永久删除数据库 [{repo_id}] 吗？内容将无法找回！(y/N): ")
    if confirm.lower() == 'y':
        try:
            api.delete_repo(repo_id=repo_id, repo_type="dataset")
            print(f"✅ 数据库 [{repo_id}] 已被彻底移除。")
        except Exception as e:
            print(f"❌ 删除失败: {e}")
    else:
        print("已取消操作。")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="HuggingFace Dataset (数据库资产) 管理专家")
    subparsers = parser.add_subparsers(dest="command")

    # list
    subparsers.add_parser("list", help="列出所有数据库")
    
    # view
    parser_view = subparsers.add_parser("view", help="查看数据库内部文件内容")
    parser_view.add_argument("id", help="Dataset ID (如 persistent-storage)")

    # create
    parser_create = subparsers.add_parser("create", help="新建数据库")
    parser_create.add_argument("name", help="数据库名称")
    parser_create.add_argument("--public", action="store_true", help="设为公开 (默认私有)")

    # delete
    parser_delete = subparsers.add_parser("delete", help="删除数据库")
    parser_delete.add_argument("id", help="要删除的数据库 ID")

    args = parser.parse_args()
    
    if args.command == "list": list_datasets()
    elif args.command == "view": view_dataset(args.id)
    elif args.command == "create": create_dataset(args.name, not args.public)
    elif args.command == "delete": delete_dataset(args.id)
    else: parser.print_help()
