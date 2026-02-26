import os
import requests
import sys

def fetch_count(url, headers):
    try:
        response = requests.get(url, headers=headers, timeout=10)
        if response.status_code == 200:
            data = response.json()
            # 返回总数和前3个项目的名称
            names = [item.get("id", "").split("/")[-1] for item in data[:3]]
            return len(data), names
    except Exception:
        pass
    return 0, []

def test_connection():
    # 尝试从系统环境变量获取 HF_TOKEN
    hf_token = os.environ.get("HF_TOKEN")
    
    if not hf_token:
        print("错误: 未找到系统环境变量 'HF_TOKEN'。请先配置您的 HuggingFace Access Token。")
        print("您可以在终端中使用以下命令临时配置 (PowerShell):")
        print("$env:HF_TOKEN=\"your_token_here\"")
        sys.exit(1)
        
    print("正在测试与 HuggingFace API 的连接并获取账户信息...\n")
    
    headers = {
        "Authorization": f"Bearer {hf_token}"
    }
    
    try:
        # 请求 Hugging Face whoami 端点
        url = "https://huggingface.co/api/whoami-v2"
        response = requests.get(url, headers=headers, timeout=10)
        
        if response.status_code == 200:
            user_data = response.json()
            username = user_data.get("name", "Unknown User")
            user_type = user_data.get("type", "user")
            email = user_data.get("email", "未公开邮箱")
            
            print(f"✅ 连接成功! 认证用户: {username} ({user_type})")
            print(f"✉️ 邮箱: {email}")
            
            # 获取并统计资源信息
            print("\n正在获取该账户的资源统计信息...")
            models_url = f"https://huggingface.co/api/models?author={username}"
            datasets_url = f"https://huggingface.co/api/datasets?author={username}"
            spaces_url = f"https://huggingface.co/api/spaces?author={username}"
            
            models_count, models_preview = fetch_count(models_url, headers)
            datasets_count, datasets_preview = fetch_count(datasets_url, headers)
            spaces_count, spaces_preview = fetch_count(spaces_url, headers)
            
            print(f"\n======== 资源总览 ========")
            print(f"🧠 模型 (Models): {models_count}")
            if models_preview:
                print(f"   -> 预览: {', '.join(models_preview)}")
                
            print(f"📊 数据集 (Datasets): {datasets_count}")
            if datasets_preview:
                print(f"   -> 预览: {', '.join(datasets_preview)}")
                
            print(f"🚀 空间 (Spaces): {spaces_count}")
            if spaces_preview:
                print(f"   -> 预览: {', '.join(spaces_preview)}")
            print(f"=========================\n")
            
        elif response.status_code == 401:
            print("❌ 认证失败: 无效的 HF_TOKEN。请检查环境变量中的 Token 是否正确且未过期。")
            sys.exit(1)
        else:
            print(f"⚠️ 连接失败，状态码: {response.status_code}")
            print(f"详情: {response.text}")
            sys.exit(1)
            
    except requests.exceptions.RequestException as e:
        print(f"❌ 网络请求异常，无法连接到 HuggingFace API: {e}")
        sys.exit(1)

if __name__ == "__main__":
    test_connection()
