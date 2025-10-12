import configparser
import re
import sys
import json
import hashlib
from pathlib import Path
import logging
from collections import defaultdict

CONFIG_FILE = Path('config.ini')
KEY_VALUE_PATTERN = re.compile(r"^\s*([\w\s.\[\]()-]+?)\s*=\s*(.*?),?\s*$")
INDEX_CACHE_FILE = Path('translation_utils/key_source_mod.json')
HASH_CACHE_FILE = Path('data/.cache/mod_content_hashes.json')

def load_config():
    """从 config.ini 加载配置。"""
    parser = configparser.ConfigParser()
    if not CONFIG_FILE.is_file():
        raise FileNotFoundError(f"错误：配置文件 '{CONFIG_FILE}' 未找到。")
    parser.read(CONFIG_FILE, encoding='utf-8')
    
    try:
        return {
            "output_path": Path(parser.get('Paths', 'output_parent_path')),
            "completed_path": Path(parser.get('Paths', 'completed_path')),
            "en_output": parser.get('Output', 'en_output_filename'),
            "cn_output": parser.get('Output', 'cn_output_filename'),
            "output": parser.get('Output', 'output_filename'),
            "en_todo": parser.get('Output', 'en_todo_filename')
        }
    except (configparser.NoSectionError, configparser.NoOptionError) as e:
        raise ValueError(f"错误：配置文件 '{CONFIG_FILE}' 中缺少必要的配置项: {e}")

def load_json_cache(path: Path) -> dict:
    """加载JSON缓存文件，并处理旧格式。"""
    if not path.is_file():
        return {}
    try:
        with open(path, 'r', encoding='utf-8') as f:
            if path.name == INDEX_CACHE_FILE.name:
                data = json.load(f)
                return {tuple(json.loads(k)): v for k, v in data.items()}
            return json.load(f)
    except (json.JSONDecodeError, TypeError):
        logging.warning(f"警告：无法解析缓存文件 '{path}'，将创建新的缓存。")
        return {}

def save_json_cache(path: Path, data: dict):
    """将数据保存到JSON缓存文件。"""
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        with open(path, 'w', encoding='utf-8') as f:
            if path.name == INDEX_CACHE_FILE.name:
                serializable_data = {json.dumps(k, ensure_ascii=False): v for k, v in data.items()}
                json.dump(serializable_data, f, indent=2, ensure_ascii=False)
            else:
                json.dump(data, f, indent=2, ensure_ascii=False)
    except Exception as e:
        logging.error(f"错误：保存缓存到 '{path}' 失败: {e}")

def get_file_hash(path: Path) -> str | None:
    """计算文件的SHA256哈希值。"""
    if not path.is_file():
        return None
    try:
        h = hashlib.sha256()
        h.update(path.read_bytes())
        return h.hexdigest()
    except Exception:
        return None

def update_index_incrementally(output_path: Path, en_output_filename: str) -> tuple[dict, dict]:
    """通过增量更新方式构建全局键值对索引。"""
    logging.info("\n--- 阶段一：扫描文件并增量更新全局索引 ---")
    
    pair_locations = load_json_cache(INDEX_CACHE_FILE)
    content_hashes = load_json_cache(HASH_CACHE_FILE)
    
    if not output_path.is_dir():
        logging.warning(f"警告：输出目录 '{output_path}' 不存在，跳过扫描。")
        return pair_locations, content_hashes

    all_mod_dirs = [d for d in output_path.iterdir() if d.is_dir()]
    updated_mods = set()

    for mod_dir in all_mod_dirs:
        mod_id_match = re.search(r'(\d+)$', mod_dir.name)
        if not mod_id_match:
            continue
        mod_id = mod_id_match.group(1)

        en_output_file = mod_dir / en_output_filename
        current_hash = get_file_hash(en_output_file)
        
        if current_hash is None: continue

        cached_hash = content_hashes.get(mod_id)

        if current_hash != cached_hash:
            logging.info(f"  -> 检测到变更: {mod_dir.name}")
            updated_mods.add(mod_id)
            
            stale_keys = []
            for pair, mod_ids in pair_locations.items():
                if mod_id in mod_ids:
                    mod_ids.remove(mod_id)
                if not mod_ids:
                    stale_keys.append(pair)
            for key in stale_keys:
                del pair_locations[key]

            try:
                content = en_output_file.read_text(encoding='utf-8')
                for line in content.splitlines():
                    match = KEY_VALUE_PATTERN.match(line.strip())
                    if match:
                        key, raw_value = match.groups()
                        pair = (key.strip(), raw_value)
                        if pair not in pair_locations:
                            pair_locations[pair] = []
                        if mod_id not in pair_locations[pair]:
                            pair_locations[pair].append(mod_id)
            except Exception as e:
                logging.error(f"    -> 读取或解析文件 '{en_output_file}' 时出错: {e}")
            
            content_hashes[mod_id] = current_hash
    
    if updated_mods:
        logging.info(f"索引更新完成：共处理了 {len(updated_mods)} 个变更的模组。")
    else:
        logging.info("未检测到文件变更，索引无需更新。")
        
    return pair_locations, content_hashes

def build_removal_map_from_index(pair_locations: dict) -> dict:
    """从全局索引构建待移除条目的映射。"""
    removal_map = defaultdict(set)
    duplicate_count = 0
    for pair, mod_ids in pair_locations.items():
        if len(mod_ids) > 1:
            duplicate_count += 1
            sorted_ids = sorted(mod_ids, key=int)
            ids_to_clean = sorted_ids[1:]
            for mod_id in ids_to_clean:
                removal_map[mod_id].add(pair)
    
    if duplicate_count > 0:
        logging.info(f"分析完成：发现 {duplicate_count} 个重复键值对，已生成清理计划。")
    return removal_map

def filter_files_in_directory(mod_dir: Path, files_to_filter: list, pairs_to_remove: set):
    """从指定目录的一系列文件中，移除重复的键值对。"""
    if not pairs_to_remove:
        logging.info("  -> 无需改动。")
        return

    for filename in files_to_filter:
        target_file = mod_dir / filename
        if not target_file.is_file():
            continue

        try:
            original_lines = target_file.read_text(encoding='utf-8').splitlines()
            filtered_lines = []
            removed_in_file = 0

            for line in original_lines:
                match = KEY_VALUE_PATTERN.match(line.strip())
                if match:
                    key, raw_value = match.groups()
                    if (key.strip(), raw_value) in pairs_to_remove:
                        removed_in_file += 1
                        continue
                filtered_lines.append(line)
            
            if removed_in_file > 0:
                content_to_write = '\n'.join(filtered_lines)
                if content_to_write or len(original_lines) > 0:
                    content_to_write += '\n'
                target_file.write_text(content_to_write, encoding='utf-8')
                logging.info(f"  -> 从 '{filename}' 中移除了 {removed_in_file} 行")

        except Exception as e:
            logging.error(f"    -> 处理文件 '{target_file}' 时出错: {e}")

def find_target_dirs(output_path: Path, target_ids: list) -> list:
    """根据Mod ID列表，在输出目录中查找对应的文件夹路径。"""
    target_dirs = []
    if not output_path.is_dir():
        return []
        
    id_to_dir_map = {
        re.search(r'(\d+)$', d.name).group(1): d
        for d in output_path.iterdir() if d.is_dir() and re.search(r'(\d+)$', d.name)
    }

    for mod_id in target_ids:
        if mod_id in id_to_dir_map:
            target_dirs.append(id_to_dir_map[mod_id])
        else:
            logging.warning(f"警告：在输出目录中未找到 Mod ID '{mod_id}' 对应的文件夹。")
            
    return target_dirs

def main():
    """主执行函数。"""
    logging.basicConfig(level=logging.INFO, format='%(message)s')
    
    try:
        config = load_config()
    except (FileNotFoundError, ValueError) as e:
        logging.error(e)
        return

    is_index_only_mode = len(sys.argv) < 2
    mod_ids_to_process = []

    if is_index_only_mode:
        logging.info("--- 以【仅索引模式】运行 ---")
    else:
        logging.info("--- 以【清理模式】运行 ---")
        try:
            mod_ids_to_process = json.loads(sys.argv[1])
            if not isinstance(mod_ids_to_process, list):
                raise ValueError("参数不是一个有效的JSON列表。")
        except (json.JSONDecodeError, ValueError) as e:
            logging.error(f"错误：解析传入的 Mod ID 列表失败: {e}")
            return

    output_path = config["output_path"]
    
    pair_locations, content_hashes = update_index_incrementally(output_path, config["en_output"])

    if not is_index_only_mode:
        logging.info("\n--- 阶段二：根据全局索引执行文件清理 ---")
        removal_map = build_removal_map_from_index(pair_locations)

        if not removal_map:
            logging.info("全局分析未发现任何重复项，无需清理。")
        else:
            logging.info("\n--- 正在根据清理计划同步更新内存中的全局索引 ---")
            cleaned_count = 0
            for mod_id in mod_ids_to_process:
                if mod_id in removal_map:
                    pairs_to_remove = removal_map[mod_id]
                    for pair in pairs_to_remove:
                        if pair in pair_locations and mod_id in pair_locations[pair]:
                            pair_locations[pair].remove(mod_id)
                            cleaned_count += 1
                            if not pair_locations[pair]:
                                del pair_locations[pair]
            
            if cleaned_count > 0:
                logging.info(f"  -> 成功从内存索引中为本次运行的模组移除了 {cleaned_count} 条记录。")
            else:
                logging.info("  -> 本次运行的模组在内存索引中无需改动。")


            target_dirs = find_target_dirs(output_path, mod_ids_to_process)
            if not target_dirs:
                logging.info("本次运行未涉及任何需要清理的有效目录。")
            else:
                files_to_filter = [config["en_output"], config["cn_output"], config["output"], config["en_todo"]]
                for mod_dir in sorted(target_dirs, key=lambda d: d.name):
                    mod_id = re.search(r'(\d+)$', mod_dir.name).group(1)
                    if mod_id in removal_map:
                        logging.info(f"-> 正在清理 output_files 中的目录: {mod_dir.name}")
                        filter_files_in_directory(mod_dir, files_to_filter, removal_map[mod_id])
                        completed_mod_dir = config["completed_path"] / mod_id
                        if completed_mod_dir.is_dir():
                            logging.info(f"-> 正在同步清理 completed_files 中的目录: {completed_mod_dir.name}")
                            filter_files_in_directory(completed_mod_dir, [config["en_todo"]], removal_map[mod_id])
                        else:
                            logging.info(f"-> 在 completed_files 中未找到对应目录 {mod_id}，跳过同步。")
                    else:
                        logging.info(f"-> 目录 '{mod_dir.name}' 在清理计划中无需改动，跳过。")

    logging.info("\n--- 阶段三：保存缓存文件 ---")
    save_json_cache(INDEX_CACHE_FILE, pair_locations)
    save_json_cache(HASH_CACHE_FILE, content_hashes)
    logging.info(f"  -> 全局索引已保存到: {INDEX_CACHE_FILE}")
    logging.info(f"  -> 内容哈希已保存到: {HASH_CACHE_FILE}")

    logging.info("\n--- 后处理脚本执行完毕 ---")

if __name__ == "__main__":
    main()
