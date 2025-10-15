#在unknown_CN.txt存在内容时运行该脚本,并人工修改unknown_classification_map.json
import json
import re
from pathlib import Path
import logging

# 获取项目根目录 (脚本位于 scripts/ 子目录下)
BASE_DIR = Path(__file__).resolve().parent.parent

# 输入文件路径
UNKNOWN_CN_FILE = BASE_DIR / 'data' / 'PZ-Mod-Translation' / 'unknown_CN.txt'

# 输出/输入文件路径 (用于增量更新)
CLASSIFICATION_MAP_FILE = BASE_DIR / 'translation_utils' / 'unknown_classification_map.json'

# --- 正则表达式 ---
# 匹配 '-- 1234567890 --' 格式的 Mod ID 行
MOD_ID_PATTERN = re.compile(r"^-+\s*(\d+)\s*-+")
# 匹配 'key = "value"' 行，并提取 key
KEY_PATTERN = re.compile(r"^\s*([\w\s.\[\]()-]+?)\s*=")

def classify_unknown_translations():
    """
    读取 unknown_CN.txt 文件，并以增量方式更新用于手动分类的 JSON 映射文件。
    只会添加新发现的、且在现有映射文件中不存在的键。
    """
    logging.info("--- 开始增量更新未知键分类文件 ---")

    if not UNKNOWN_CN_FILE.is_file() or UNKNOWN_CN_FILE.stat().st_size == 0:
        logging.info(f"  -> 文件 '{UNKNOWN_CN_FILE.name}' 不存在或为空，跳过处理。")
        return

    try:
        content = UNKNOWN_CN_FILE.read_text(encoding='utf-8')
    except Exception as e:
        logging.error(f"  -> 错误：读取文件 '{UNKNOWN_CN_FILE.name}' 失败: {e}")
        return

    # --- 1. 从 unknown_CN.txt 解析当前所有未知键 ---
    current_unknowns = {}
    current_mod_id = None
    for line in content.splitlines():
        line_stripped = line.strip()
        if not line_stripped:
            continue

        mod_id_match = MOD_ID_PATTERN.match(line_stripped)
        if mod_id_match:
            current_mod_id = mod_id_match.group(1)
            if current_mod_id not in current_unknowns:
                current_unknowns[current_mod_id] = set()
            continue

        if current_mod_id:
            key_match = KEY_PATTERN.match(line_stripped)
            if key_match:
                key = key_match.group(1).strip()
                current_unknowns[current_mod_id].add(key)

    if not current_unknowns:
        logging.info("  -> 未在文件中找到任何有效的 Mod ID 或键，处理结束。")
        return

    # --- 2. 读取现有的分类文件 ---
    CLASSIFICATION_MAP_FILE.parent.mkdir(parents=True, exist_ok=True)
    existing_data = {}
    if CLASSIFICATION_MAP_FILE.is_file():
        try:
            with open(CLASSIFICATION_MAP_FILE, 'r', encoding='utf-8') as f:
                existing_data = json.load(f)
        except (json.JSONDecodeError, Exception):
            logging.warning(f"  -> 警告：无法解析现有的 '{CLASSIFICATION_MAP_FILE.name}'，将创建一个全新的文件。")
            existing_data = {}

    # --- 3. 对比并只添加新键  ---
    update_count = 0
    for mod_id, keys_set in current_unknowns.items():
        if mod_id not in existing_data:
            existing_data[mod_id] = {}
        
        for key in keys_set:
            # 只有当 key 不在现有映射中时，才添加
            if key not in existing_data[mod_id]:
                placeholder = f"CLASSIFY_UNKNOWN_{mod_id}"
                existing_data[mod_id][key] = placeholder
                update_count += 1
    
    if update_count == 0:
        logging.info("  -> 无新发现的未知键需要添加。分类文件已是最新。")
        return

    # --- 4. 写回更新后的文件 ---
    try:
        with open(CLASSIFICATION_MAP_FILE, 'w', encoding='utf-8') as f:
            json.dump(existing_data, f, ensure_ascii=False, indent=2)
        logging.info(f"  -> 成功更新分类文件 '{CLASSIFICATION_MAP_FILE.name}'。新增 {update_count} 个待分类条目。")
    except Exception as e:
        logging.error(f"  -> 错误：写入文件 '{CLASSIFICATION_MAP_FILE.name}' 失败: {e}")

def main():
    logging.basicConfig(level=logging.INFO, format='%(message)s')
    classify_unknown_translations()

if __name__ == "__main__":
    main()
