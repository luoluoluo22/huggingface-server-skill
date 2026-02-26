import os
import requests
import sys
import argparse
import concurrent.futures

def get_headers():
    hf_token = os.environ.get("HF_TOKEN")
    if not hf_token:
        print("错误: 未找到系统环境变量 'HF_TOKEN'。")
        print("请在系统环境变量或终端中设置 HF_TOKEN。")
        sys.exit(1)
    return {"Authorization": f"Bearer {hf_token}"}

def get_username(headers):
    # 获取当前认证用户的名字
    url = "https://huggingface.co/api/whoami-v2"
    response = requests.get(url, headers=headers, timeout=10)
    if response.status_code == 200:
        return response.json().get("name")
    else:
        print(f"获取账户信息失败: {response.status_code} {response.text}")
        sys.exit(1)

def fetch_space_detail(space_id, headers):
    url = f"https://huggingface.co/api/spaces/{space_id}"
    response = requests.get(url, headers=headers, timeout=10)
    if response.status_code == 200:
        return response.json()
    return None

def list_spaces():
    headers = get_headers()
    username = get_username(headers)
    print(f"正在拉取 {username} 的 Spaces 列表与状态（可能需要几秒钟）...\n")
    
    url = f"https://huggingface.co/api/spaces?author={username}"
    response = requests.get(url, headers=headers, timeout=20)
    
    if response.status_code == 200:
        spaces = response.json()
        print(f"{'Space 名称':<20} | {'运行状态/Stage':<18} | {'私有':<5} | {'Space 主页 URL':<45} | {'Direct App URL'}")
        print("-" * 150)
        
        # 使用并发获取每个 space 的详细状态
        with concurrent.futures.ThreadPoolExecutor(max_workers=5) as executor:
            future_to_space = {executor.submit(fetch_space_detail, space.get("id"), headers): space for space in spaces}
            
            detailed_spaces = []
            for future in concurrent.futures.as_completed(future_to_space):
                detail = future.result()
                if detail:
                    detailed_spaces.append(detail)
                else:
                    detailed_spaces.append(future_to_space[future])
        
        # 按照名称排序
        detailed_spaces.sort(key=lambda x: x.get("id", ""))
        
        for space in detailed_spaces:
            repo_id = space.get("id", "")
            name = repo_id.split("/")[-1]
            runtime = space.get("runtime", {})
            stage = runtime.get("stage", "UNKNOWN")
            is_private = "Yes" if space.get("private") else "No"
            
            # 使用简单的符号标记状态
            status_symbol = "🟢" if stage == "RUNNING" else ("🔴" if "ERROR" in stage else ("🟡" if "BUILDING" in stage else ("⏸️" if stage == "PAUSED" or stage == "STOPPED" else "⚪")))
            
            # 拼接主页 URL 
            space_url = f"https://huggingface.co/spaces/{repo_id}"
            
            # 解析 direct app url (即 iframe 真实嵌入或直连地址)
            direct_url = space.get("host") if space.get("host") else f"https://{repo_id.replace('/', '-')}.hf.space"
            
            print(f"{name:<20} | {status_symbol} {stage:<16} | {is_private:<5} | {space_url:<45} | {direct_url}")
            
        print("-" * 150)
        print(f"共检索到 {len(spaces)} 个 Spaces。\n")
    else:
        print(f"获取 Spaces 列表失败: {response.text}")

def action_space(space_id, action):
    headers = get_headers()
    if "/" not in space_id:
        username = get_username(headers)
        repo_id = f"{username}/{space_id}"
    else:
        repo_id = space_id
        
    print(f"正在尝试对 [{repo_id}] 执行 [{action}] 操作...")
    url = f"https://huggingface.co/api/spaces/{repo_id}/{action}"
    
    response = requests.post(url, headers=headers, timeout=15)
    
    if response.status_code in [200, 202]:
        try:
            res_json = response.json()
            if "error" in res_json:
                print(f"❌ 出现异常: {res_json['error']}")
            else:
                print(f"✅ 操作成功！请求已发送到远端服务器。")
        except:
            print(f"✅ 成功发送指令给 {repo_id} (服务器已接收)。")
    elif response.status_code == 404:
        print(f"❌ 找不到资源 (404): 请检查 Space '{repo_id}' 是否存在。")
    elif response.status_code == 401:
        print(f"❌ 无权限 (401): 您的 Token 似乎没有写入该资源或者操作该资源的权限。")
    else:
        print(f"❌ 操作失败 (状态码 {response.status_code}): {response.text}")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="HuggingFace Space 管理工具")
    subparsers = parser.add_subparsers(dest="command", help="支持的命令: list, restart")
    
    parser_list = subparsers.add_parser("list", help="列出账户下所有 Spaces 及其当前运行状态")
    
    parser_restart = subparsers.add_parser("restart", help="重启指定的 Space 容器")
    parser_restart.add_argument("space_id", help="Space 名称 (例如 'my-app' 或 'username/my-app')")
    
    args = parser.parse_args()
    
    if args.command == "list":
        list_spaces()
    elif args.command == "restart":
        action_space(args.space_id, "restart")
    else:
        parser.print_help()
