import os
import json
import requests
from pathlib import Path

# --- 配置 ---
ID_LIST_FILE = Path("id_list.txt")
TIMESTAMP_FILE = Path("translation_utils/mod_timestamps.json")

def main():
    # 确保目录存在
    TIMESTAMP_FILE.parent.mkdir(parents=True, exist_ok=True)

    api_key = os.environ.get("STEAM_API_KEY")
    if not api_key:
        print("错误: 环境变量 STEAM_API_KEY 未设置！")
        exit(1)

    if not ID_LIST_FILE.exists():
        print(f"错误: 文件 {ID_LIST_FILE} 不存在！")
        exit(1)

    if TIMESTAMP_FILE.exists():
        try:
            old_stamps = json.loads(TIMESTAMP_FILE.read_text(encoding='utf-8'))
        except json.JSONDecodeError:
            print(f"警告: 文件 {TIMESTAMP_FILE} 内容不是有效的JSON，将使用空数据。")
            old_stamps = {}
    else:
        old_stamps = {}

    raw_mod_ids = [line.strip() for line in ID_LIST_FILE.read_text(encoding='utf-8').splitlines() if line.strip().isdigit()]
    
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

    all_mod_details = []
    chunk_size = 18

    for i in range(0, len(mod_ids), chunk_size):
        chunk = mod_ids[i:i + chunk_size]
        print(f"正在查询第 {i//chunk_size + 1} 批 Mod (共 {len(chunk)} 个)...")
        
        try:
            payload = {
                "itemcount": len(chunk),
                **{f"publishedfileids[{j}]": mod_id for j, mod_id in enumerate(chunk)}
            }
            response = requests.post(
                f"https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
                params={"key": api_key},
                data=payload
            )
            response.raise_for_status()
            data = response.json()
            
            details = data.get("response", {}).get("publishedfiledetails", [])
            if details:
                all_mod_details.extend(details)
            else:
                print(f"警告: API响应格式不正确或未返回该批次的Mod详情。")
                print("收到的响应:", data)

        except requests.RequestException as e:
            print(f"错误: 调用Steam API失败 (批次 {i//chunk_size + 1}): {e}")
            continue

    if not all_mod_details:
        print("错误: 所有API请求均失败或未返回任何有效的Mod详情。")
        exit(1)

    mods_to_download = []
    updated_stamps = old_stamps.copy()

    for detail in all_mod_details:
        if detail.get("result") == 1:
            mod_id = detail["publishedfileid"]
            new_time = detail["time_updated"]
            old_time = int(old_stamps.get(mod_id) or 0)

            print(f"检查 [ID: {mod_id}]: 最新时间戳={new_time}, 已记录时间戳={old_time}")
            if new_time > old_time:
                print("  -> 需要更新。")
                mods_to_download.append(mod_id)
                updated_stamps[mod_id] = new_time
            else:
                print("  -> 无需更新。")
    
    try:
        TIMESTAMP_FILE.write_text(json.dumps(updated_stamps, indent=4), encoding='utf-8')
        print(f"时间戳文件 {TIMESTAMP_FILE} 已更新。")
    except IOError as e:
        print(f"错误: 无法写入时间戳文件 {TIMESTAMP_FILE}: {e}")


    output_to_github(mods_to_download)

def output_to_github(id_list):
    output_path_str = os.environ.get("GITHUB_OUTPUT")
    if not output_path_str:
        print("非 GitHub Actions 环境，将结果打印到控制台。")
        print(f"mods_to_download={json.dumps(id_list)}")
        return

    output_path = Path(output_path_str)
    json_output = json.dumps(id_list)
    print(f"需要更新的Mod: {json_output}")
    try:
        with output_path.open("a") as f:
            f.write(f"mods_to_download={json_output}\n")
    except IOError as e:
        print(f"错误: 无法写入 GitHub output 文件: {e}")


if __name__ == "__main__":
    main()
