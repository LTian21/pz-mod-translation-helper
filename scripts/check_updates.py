import os
import json
import requests
from pathlib import Path

def main():
    api_key = os.environ.get("STEAM_API_KEY")
    if not api_key:
        print("错误: 环境变量 STEAM_API_KEY 未设置！")
        exit(1)

    id_list_file = Path("id_list.txt")
    if not id_list_file.exists():
        print(f"错误: 文件 {id_list_file} 不存在！")
        exit(1)

    data_dir = Path("data")
    data_dir.mkdir(exist_ok=True)
    timestamp_file = Path("mod_timestamps.json")
    if timestamp_file.exists():
        old_stamps = json.loads(timestamp_file.read_text(encoding='utf-8'))
    else:
        old_stamps = {}

    raw_mod_ids = [line.strip() for line in id_list_file.read_text(encoding='utf-8').splitlines() if line.strip().isdigit()]
    
    seen_ids = set()
    unique_mod_ids = []
    duplicates = []
    
    for mod_id in raw_mod_ids:
        if mod_id not in seen_ids:
            seen_ids.add(mod_id)
            unique_mod_ids.append(mod_id)
        else:
            duplicates.append(mod_id)
            
    if duplicates:
        unique_duplicates = sorted(list(set(duplicates)))
        print(f"警告: 在 id_list.txt 中发现 {len(duplicates)} 个重复的ID。重复项已被自动忽略。")
        print(f"  -> 重复的ID是: {', '.join(unique_duplicates)}")
    
    mod_ids = unique_mod_ids
    if not mod_ids:
        print("ID列表中没有有效的Mod ID。")
        output_to_github([])
        return

    print(f"去重后，准备查询 {len(mod_ids)} 个唯一的 Mod。")

    try:
        payload = {
            "itemcount": len(mod_ids),
            **{f"publishedfileids[{i}]": mod_id for i, mod_id in enumerate(mod_ids)}
        }
        response = requests.post(
            f"https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/?key={api_key}",
            data=payload
        )
        response.raise_for_status()
        data = response.json()
    except requests.RequestException as e:
        print(f"错误: 调用Steam API失败: {e}")
        exit(1)
    
    mod_details = data.get("response", {}).get("publishedfiledetails", [])
    if not mod_details:
        print("错误: API响应格式不正确或未返回任何Mod详情。")
        print("收到的响应:", data)
        exit(1)

    mods_to_download = []
    for detail in mod_details:
        if detail.get("result") == 1:
            mod_id = detail["publishedfileid"]
            new_time = detail["time_updated"]
            old_time = old_stamps.get(mod_id, 0)

            print(f"检查 [ID: {mod_id}]: 最新时间戳={new_time}, 已记录时间戳={old_time}")
            if new_time > int(old_time): # 确保比较的是整数
                print("  -> 需要更新。")
                mods_to_download.append(mod_id)
            else:
                print("  -> 无需更新。")

    output_to_github(mods_to_download)

def output_to_github(id_list):
    output_path = Path(os.environ["GITHUB_OUTPUT"])
    json_output = json.dumps(id_list)
    print(f"需要更新的Mod: {json_output}")
    with output_path.open("a") as f:
        f.write(f"mods_to_download={json_output}\n")

if __name__ == "__main__":
    main()