import os
import sys
import argparse
import concurrent.futures
from huggingface_hub import HfApi, SpaceRuntime

def get_api():
    hf_token = os.environ.get("HF_TOKEN")
    if not hf_token:
        print("错误: 未找到系统环境变量 'HF_TOKEN'。")
        print("请在系统环境变量或终端中设置 HF_TOKEN。")
        sys.exit(1)
    return HfApi(token=hf_token)

def get_username(api):
    try:
        user_info = api.whoami()
        return user_info.get("name")
    except Exception as e:
        print(f"获取账户信息失败: {e}")
        sys.exit(1)

def fetch_runtime_and_merge(space, api):
    try:
        # 获取最新的运行时信息
        return space.id, api.get_space_runtime(repo_id=space.id)
    except:
        return space.id, None

def list_spaces(target_space_id=None):
    api = get_api()
    username = get_username(api)
    
    try:
        if target_space_id:
            # 获取单个特定的 Space 状态
            if "/" not in target_space_id:
                repo_id = f"{username}/{target_space_id}"
            else:
                repo_id = target_space_id
            
            print(f"正在获取 [{repo_id}] 的实时状态...")
            # 获取 Space 基本信息 (为了获取 name, private 等)
            space_info = api.repo_info(repo_id=repo_id, repo_type="space")
            runtime = api.get_space_runtime(repo_id=repo_id)
            spaces = [space_info]
            runtime_map = {repo_id: runtime}
        else:
            # 获取全部列表
            print(f"正在拉取 {username} 的 Spaces 列表与实时状态...\n")
            spaces = list(api.list_spaces(author=username))
            
            # 并发获取运行时信息以提高速度
            runtime_map = {}
            with concurrent.futures.ThreadPoolExecutor(max_workers=10) as executor:
                future_to_id = {executor.submit(fetch_runtime_and_merge, space, api): space.id for space in spaces}
                for future in concurrent.futures.as_completed(future_to_id):
                    sid, runtime = future.result()
                    runtime_map[sid] = runtime

        print(f"{'Space 名称':<25} | {'运行状态/Stage':<18} | {'私有':<5} | {'Space 主页 URL':<45} | {'Direct App URL'}")
        print("-" * 155)
        
        # 使用 id 或 modelId (兼容 repo_info 返回的不同接口)
        sorted_spaces = sorted(spaces, key=lambda x: getattr(x, 'id', getattr(x, 'modelId', '')))
        
        for space in sorted_spaces:
            repo_id = getattr(space, 'id', getattr(space, 'modelId', ''))
            name = repo_id.split("/")[-1]
            
            runtime = runtime_map.get(repo_id)
            stage = runtime.stage if runtime else "UNKNOWN"
            is_private = "Yes" if space.private else "No"
            
            status_symbol = "🟢" if stage == "RUNNING" else ("🔴" if "ERROR" in stage else ("🟡" if "BUILDING" in stage else ("⏸️" if stage == "PAUSED" or stage == "STOPPED" else "⚪")))
            space_url = f"https://huggingface.co/spaces/{repo_id}"
            
            host = getattr(runtime, 'host', None) if runtime else None
            direct_url = f"https://{host}" if host else f"https://{repo_id.replace('/', '-')}.hf.space"
            
            print(f"{name:<25} | {status_symbol} {stage:<16} | {is_private:<5} | {space_url:<45} | {direct_url}")
            
        print("-" * 155)
        if not target_space_id:
            print(f"共检索到 {len(sorted_spaces)} 个 Spaces。\n")
    except Exception as e:
        print(f"获取状态信息失败: {e}")

def action_space(space_id, action):
    api = get_api()
    if "/" not in space_id:
        username = get_username(api)
        repo_id = f"{username}/{space_id}"
    else:
        repo_id = space_id
        
    print(f"正在尝试对 [{repo_id}] 执行 [{action}] 操作...")
    try:
        if action == "restart":
            api.restart_space(repo_id=repo_id)
        elif action == "pause":
            api.pause_space(repo_id=repo_id)
        elif action == "wakeup":
            api.restart_space(repo_id=repo_id) # wakeup 通常通过 restart 触发
        print(f"✅ 操作 [{action}] 成功！")
    except Exception as e:
        print(f"❌ 操作失败: {e}")

def create_space(space_name, sdk, is_private):
    api = get_api()
    print(f"正在创建新的 Space: {space_name} (SDK: {sdk}, 私密: {is_private})...")
    try:
        repo_url = api.create_repo(
            repo_id=space_name,
            repo_type="space",
            space_sdk=sdk,
            private=is_private
        )
        print(f"✅ Space '{space_name}' 创建成功！")
        print(f"➜ 链接: {repo_url}")
    except Exception as e:
        print(f"❌ 创建失败: {e}")

def delete_space(space_id):
    api = get_api()
    if "/" not in space_id:
        username = get_username(api)
        repo_id = f"{username}/{space_id}"
    else:
        repo_id = space_id
        
    print(f"⚠️ 正在尝试删除 Space: [{repo_id}] ...")
    try:
        api.delete_repo(repo_id=repo_id, repo_type="space")
        print(f"✅ Space [{repo_id}] 已成功删除。")
    except Exception as e:
        print(f"❌ 删除失败: {e}")

def manage_config(space_id, category, key, value=None):
    api = get_api()
    if "/" not in space_id:
        username = get_username(api)
        repo_id = f"{username}/{space_id}"
    else:
        repo_id = space_id

    try:
        if category == "secrets":
            if value is None:
                # 官方库目前支持列出 Secret 键名 (如果版本支持且权限足够)
                print(f"正在拉取 [{repo_id}] 的 Secrets 列表...")
                secrets = api.list_space_secrets(repo_id=repo_id)
                if not secrets:
                    print("没有找到任何 Secrets。")
                else:
                    for s in secrets:
                        print(f" - {s}") # s 通常是字符串
            else:
                print(f"正在设置 [{repo_id}] 的 Secret: {key} ...")
                api.add_space_secret(repo_id=repo_id, key=key, value=value)
                print(f"✅ Secret '{key}' 设置成功！")
        
        elif category == "variables":
            if value is None:
                print(f"正在拉取 [{repo_id}] 的 Variables 列表...")
                variables = api.get_space_variables(repo_id=repo_id)
                if not variables:
                    print("没有找到任何 Variables。")
                else:
                    for k, v in variables.items():
                        print(f" - {k}: {v}")
            else:
                print(f"正在设置 [{repo_id}] 的 Variable: {key} = {value}...")
                api.add_space_variable(repo_id=repo_id, key=key, value=value)
                print(f"✅ Variable '{key}' 设置成功！")
                
    except Exception as e:
        print(f"❌ 配置管理失败: {e}")
        if "404" in str(e) and category == "secrets":
            print("提示: 某些版本或权限可能不支持列出 Secret 名，但您可以直接进行设置。")

def get_logs(space_id, log_type="run"):
    """
    通过逆向封装的 SSE 接口流式获取日志
    支持 log_type: 'build' (构建日志) 或 'run' (运行日志)
    注：Container 日志在官方 API 路径中映射为 'run'
    """
    api = get_api()
    if "/" not in space_id:
        username = get_username(api)
        repo_id = f"{username}/{space_id}"
    else:
        repo_id = space_id
    
    print(f"正在建立 SSE 连接，获取 [{repo_id}] 的 {log_type} 日志...")
    
    # 逆向得到的 API 路径: build 日志用 /logs/build, 运行日志用 /logs/run
    url = f"https://huggingface.co/api/spaces/{repo_id}/logs/{log_type}"
    headers = {
        "Authorization": f"Bearer {api.token}",
        "Accept": "text/event-stream",
    }
    
    try:
        import requests
        import json
        
        # 使用 requests 的流式模式 (stream=True)
        # 注意: SSE 返回的是持续不断的文本流
        with requests.get(url, headers=headers, stream=True, timeout=120) as r:
            if r.status_code != 200:
                print(f"❌ 无法建立流式连接 (HTTP {r.status_code})")
                try:
                    error_info = r.json()
                    print(f"原提示: {error_info.get('error', r.text)}")
                except:
                    print(f"原提示: {r.text}")
                return

            print(f"✅ 连接成功。正在输出内容 (按 Ctrl+C 停止):\n" + "-"*30)
            
            for line in r.iter_lines():
                if line:
                    decoded_line = line.decode('utf-8')
                    # 只有以 data: 开头的行才包含实际日志内容
                    if decoded_line.startswith('data: '):
                        data_str = decoded_line[len('data: '):].strip()
                        
                        try:
                            # 尝试解析 JSON 格式 (某些日志行是结构化的)
                            payload = json.loads(data_str)
                            # 提取日志文本 (HF 日志常用 'data', 'message' 或 'msg')
                            log_msg = payload.get('data') or payload.get('message') or payload.get('msg') or data_str
                            print(log_msg)
                        except json.JSONDecodeError:
                            # 如果不是 JSON，则直接打印
                            print(data_str)
                            
    except KeyboardInterrupt:
        print("\n🛑 已停止日志输出。")
    except Exception as e:
        print(f"❌ 流式获取日志发生异常: {e}")

def manage_hardware(space_id, flavor=None):
    api = get_api()
    if "/" not in space_id:
        username = get_username(api)
        repo_id = f"{username}/{space_id}"
    else:
        repo_id = space_id
    
    try:
        if flavor is None:
            runtime = api.get_space_runtime(repo_id=repo_id)
            print(f"[{repo_id}] 当前硬件规格: {runtime.hardware}")
        else:
            print(f"请求切换硬件至: {flavor}...")
            api.request_space_hardware(repo_id=repo_id, hardware=flavor)
            print("✅ 硬件申请已提交。")
    except Exception as e:
        print(f"❌ 硬件管理失败: {e}")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="HuggingFace Space 官方 SDK 管理专家")
    subparsers = parser.add_subparsers(dest="command")

    parser_list = subparsers.add_parser("list", help="列表展示状态与地址")
    parser_list.add_argument("space_id", nargs="?", help="查看特定 Space 详情 (可选)")
    
    parser_action = subparsers.add_parser("action", help="生命周期管控")
    parser_action.add_argument("space_id")
    parser_action.add_argument("op", choices=["restart", "pause", "wakeup"])

    parser_create = subparsers.add_parser("create", help="新建 Space")
    parser_create.add_argument("name")
    parser_create.add_argument("--sdk", choices=["gradio", "streamlit", "docker", "static"], default="docker")
    parser_create.add_argument("--public", action="store_true")

    parser_config = subparsers.add_parser("config", help="配置变量或秘密")
    parser_config.add_argument("space_id")
    parser_config.add_argument("--type", choices=["secrets", "variables"], default="variables")
    parser_config.add_argument("--get", action="store_true")
    parser_config.add_argument("--key")
    parser_config.add_argument("--val")

    parser_logs = subparsers.add_parser("logs", help="实时查看日志")
    parser_logs.add_argument("space_id")
    parser_logs.add_argument("--build", action="store_true", help="查看构建日志 (默认为运行日志)")

    parser_hw = subparsers.add_parser("hardware", help="硬件规格切换")
    parser_hw.add_argument("space_id")
    parser_hw.add_argument("--set")

    parser_delete = subparsers.add_parser("delete", help="删除 Space")
    parser_delete.add_argument("space_id")

    args = parser.parse_args()
    
    if args.command == "list": list_spaces(args.space_id)
    elif args.command == "action": action_space(args.space_id, args.op)
    elif args.command == "create": create_space(args.name, args.sdk, not args.public)
    elif args.command == "delete": delete_space(args.space_id)
    elif args.command == "config":
        manage_config(args.space_id, args.type, args.key, args.val)
    elif args.command == "logs":
        log_type = "build" if args.build else "run"
        get_logs(args.space_id, log_type)
    elif args.command == "hardware": manage_hardware(args.space_id, args.set)
    else: parser.print_help()
