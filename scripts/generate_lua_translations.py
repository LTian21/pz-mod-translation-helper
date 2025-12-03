import re
import json
import os
import logging
from pathlib import Path

# 配置日志
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')

def escape_lua_string(value):
    """对字符串进行LUA转义，处理引号和反斜杠。"""
    value = value.replace('\\', '\\\\')
    value = value.replace('"', '\\"')
    return value

def load_conflict_keys(repo_dir):
    """加载所有冲突的键。"""
    conflict_keys = set()
    
    # 1. 加载模组间冲突键
    inter_mod_conflict_path = repo_dir / 'warnings' / 'conflict_keys.txt'
    logging.info(f"读取模组间冲突文件: {inter_mod_conflict_path}")
    try:
        with open(inter_mod_conflict_path, 'r', encoding='utf-8') as f:
            for line in f:
                if line.startswith('Conflict key:'):
                    key = line.replace('Conflict key:', '').strip()
                    conflict_keys.add(key)
    except FileNotFoundError:
        logging.warning(f"模组间冲突文件未找到: {inter_mod_conflict_path}")

    # 2. 加载模组与原版冲突键
    mod_vanilla_conflict_dir = repo_dir / 'data' / 'output_files'
    logging.info(f"扫描模组与原版冲突目录: {mod_vanilla_conflict_dir}")
    for root, _, files in os.walk(mod_vanilla_conflict_dir):
        if 'conflict_keys.txt' in files:
            file_path = Path(root) / 'conflict_keys.txt'
            try:
                with open(file_path, 'r', encoding='utf-8') as f:
                    for line in f:
                        match = re.match(r'^\s*([^=]+?)\s*=', line)
                        if match:
                            key = match.group(1).strip()
                            conflict_keys.add(key)
            except Exception as e:
                logging.error(f"处理文件 {file_path} 时出错: {e}")
                
    logging.info(f"共加载 {len(conflict_keys)} 个唯一的冲突键。")
    return conflict_keys

def main():
    """主函数，用于生成LUA翻译文件。"""
    try:
        repo_dir = Path(__file__).parent.parent.resolve()
        
        translations_path = repo_dir / 'data' / 'translations_CN.txt'
        map_path = repo_dir / 'translation_utils' / 'workshop_id_to_mod_id_map.json'
        output_dir = repo_dir / 'data'
        output_path = output_dir / 'PZ_Mod_Translations_CN.lua'

        logging.info(f"项目根目录: {repo_dir}")

        # 1. 加载所有冲突键
        conflict_keys = load_conflict_keys(repo_dir)

        # 2. 加载 Workshop ID -> [ModID列表] 映射
        logging.info(f"读取ID映射文件: {map_path}")
        with open(map_path, 'r', encoding='utf-8') as f:
            workshop_to_mod_ids_map = json.load(f)
        logging.info("成功加载 Workshop ID 到 Mod ID 列表的映射。")

        # 3. 解析翻译文件并根据冲突键进行过滤
        translations = {}
        translation_regex = re.compile(r'^\s*(?P<workshop_id>\d+)::CN::(?P<key>[^=]+?)\s*=\s*"(?P<value>.*)"\s*,?\s*$')
        
        logging.info(f"开始解析翻译文件: {translations_path}")
        with open(translations_path, 'r', encoding='utf-8') as f:
            for line in f:
                match = translation_regex.match(line)
                if match:
                    key = match.group('key').strip()
                    # 只有当键是冲突键时才处理
                    if key in conflict_keys:
                        workshop_id = match.group('workshop_id')
                        value = match.group('value')

                        associated_mod_ids = workshop_to_mod_ids_map.get(workshop_id)
                        if associated_mod_ids:
                            for mod_id in associated_mod_ids:
                                if mod_id not in translations:
                                    translations[mod_id] = {}
                                translations[mod_id][key] = value
        logging.info(f"翻译文件解析完成，共为 {len(translations)} 个Mod ID 准备了冲突翻译数据。")

        # 4. 生成LUA文件内容
        lua_content = [
            "-- 由自动化脚本生成，不要手动修改",
            "-- 格式：[ModID] = { [Key] = \"Value\" }\n",
            "return {"
        ]

        sorted_mod_ids = sorted(translations.keys())

        for mod_id in sorted_mod_ids:
            mod_translation_lines = []
            sorted_keys = sorted(translations[mod_id].keys())
            for key in sorted_keys:
                value = translations[mod_id][key]
                if value: # 如果翻译值不为空字符串，才输出该条目
                    escaped_value = escape_lua_string(value)
                    mod_translation_lines.append(f'        ["{key}"] = "{escaped_value}",')
            
            if mod_translation_lines: # 仅当该Mod存在有效翻译时，才将其添加到LUA文件中
                lua_content.append(f'    ["\\\\{mod_id}"] = {{')
                lua_content.extend(mod_translation_lines)
                lua_content.append('    },')

        lua_content.append("}")

        # 5. 写入输出文件
        with open(output_path, 'w', encoding='utf-8') as f:
            f.write('\n'.join(lua_content))

        logging.info(f"成功生成LUA翻译文件: {output_path}")

    except FileNotFoundError as e:
        logging.error(f"文件未找到: {e}")
    except json.JSONDecodeError as e:
        logging.error(f"JSON解析错误: {e}")
    except Exception as e:
        logging.error(f"发生未知错误: {e}", exc_info=True)

if __name__ == "__main__":
    main()
