import re
import json
import os
import logging

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
        # 计算项目根目录
        repo_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        
        # 定义文件路径
        translations_path = os.path.join(repo_dir, 'data', 'translations_CN.txt')
        map_path = os.path.join(repo_dir, 'translation_utils', 'workshop_id_to_mod_id_map.json')
        output_dir = os.path.join(repo_dir, 'data')
        output_path = os.path.join(output_dir, 'PZ_Mod_Translations_CN.lua')

        logging.info(f"项目根目录: {repo_dir}")
        logging.info(f"读取翻译文件: {translations_path}")
        logging.info(f"读取ID映射文件: {map_path}")

        # 确保输出目录存在
        if not os.path.exists(output_dir):
            os.makedirs(output_dir)
            logging.info(f"创建输出目录: {output_dir}")

        # 加载 Workshop ID 到 ModID 的映射
        with open(map_path, 'r', encoding='utf-8') as f:
            workshop_to_mod_map = json.load(f)
        logging.info("成功加载 Workshop ID 到 ModID 的映射。")

        translations = {}
        current_workshop_id = None

        # 用于解析中文翻译行的正则表达式
        translation_regex = re.compile(r'^\s*(?P<workshop_id>\d+)::CN::(?P<key>[^=]+?)\s*=\s*"(?P<value>.*)"\s*,?\s*$')
        header_regex = re.compile(r'^\s*------\s*(\d+)\s*::.*')

        logging.info("开始解析翻译文件...")
        with open(translations_path, 'r', encoding='utf-8') as f:
            for line in f:
                header_match = header_regex.match(line)
                if header_match:
                    current_workshop_id = header_match.group(1)
                    continue

                match = translation_regex.match(line)
                if match and current_workshop_id:
                    workshop_id = match.group('workshop_id')
                    if workshop_id != current_workshop_id:
                        current_workshop_id = workshop_id

                    key = match.group('key').strip()
                    value = match.group('value')

                    mod_id = workshop_to_mod_map.get(current_workshop_id)

                    if mod_id:
                        if mod_id not in translations:
                            translations[mod_id] = {}
                        translations[mod_id][key] = value
        logging.info(f"翻译文件解析完成，共处理了 {len(translations)} 个Mod。")

        # 生成LUA文件内容
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
                # 如果翻译值不为空字符串，才输出该条目
                if value:
                    escaped_value = escape_lua_string(value)
                    mod_translation_lines.append(f'        ["{key}"] = "{escaped_value}",')
            
            # 仅当该Mod存在有效翻译时，才将其添加到LUA文件中
            if mod_translation_lines:
                lua_content.append(f'    ["\\\\{mod_id}"] = {{')
                lua_content.extend(mod_translation_lines)
                lua_content.append('    },')

        lua_content.append("}")

        # 写入输出文件
        with open(output_path, 'w', encoding='utf-8') as f:
            f.write('\n'.join(lua_content))

        logging.info(f"成功生成LUA翻译文件: {output_path}")

    except FileNotFoundError as e:
        logging.error(f"文件未找到: {e}")
    except json.JSONDecodeError as e:
        logging.error(f"JSON解析错误: {e}")
    except Exception as e:
        logging.error(f"发生未知错误: {e}")

if __name__ == "__main__":
    main()
