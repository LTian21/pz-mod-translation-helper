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

def main():
    """主函数，用于生成LUA翻译文件。"""
    try:
        repo_dir = Path(__file__).parent.parent.resolve()
        
        translations_path = repo_dir / 'data' / 'translations_CN.txt'
        map_path = repo_dir / 'translation_utils' / 'workshop_id_to_mod_id_map.json'
        output_dir = repo_dir / 'data'
        output_path = output_dir / 'PZ_Mod_Translations_CN.lua'

        logging.info(f"项目根目录: {repo_dir}")
        logging.info(f"读取翻译文件: {translations_path}")
        logging.info(f"读取ID映射文件: {map_path}")

        # 1. 加载新格式的 Workshop ID -> [ModID列表] 映射
        with open(map_path, 'r', encoding='utf-8') as f:
            workshop_to_mod_ids_map = json.load(f)
        logging.info("成功加载 Workshop ID 到 Mod ID 列表的映射。")

        # 2. 解析翻译文件
        translations = {}
        translation_regex = re.compile(r'^\s*(?P<workshop_id>\d+)::CN::(?P<key>[^=]+?)\s*=\s*"(?P<value>.*)"\s*,?\s*$')
        
        logging.info("开始解析翻译文件...")
        with open(translations_path, 'r', encoding='utf-8') as f:
            for line in f:
                match = translation_regex.match(line)
                if match:
                    workshop_id = match.group('workshop_id')
                    key = match.group('key').strip()
                    value = match.group('value')

                    # 查找此 Workshop ID 关联的所有 Mod ID
                    associated_mod_ids = workshop_to_mod_ids_map.get(workshop_id)

                    if associated_mod_ids:
                        # 将此翻译复制到所有关联的 Mod ID 下
                        for mod_id in associated_mod_ids:
                            if mod_id not in translations:
                                translations[mod_id] = {}
                            translations[mod_id][key] = value
        logging.info(f"翻译文件解析完成，共为 {len(translations)} 个Mod ID 准备了翻译数据。")

        # 3. 生成LUA文件内容
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

        # 4. 写入输出文件
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
