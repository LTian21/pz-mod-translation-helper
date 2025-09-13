import configparser
import json
import logging
import re
import subprocess
import io
import sys
from datetime import datetime
from pathlib import Path
from typing import Final

CONFIG_FILE: Final[Path] = Path('config.ini')
ID_LIST_FILE: Final[Path] = Path('id_list.txt')
STATUS_FILE: Final[Path] = Path('data/.cache/.last_run_status.json')

VERSION_DIR_PATTERN: Final[re.compile] = re.compile(r'^\d+(\.\d+)*$')
MODULE_PATTERN: Final[re.compile] = re.compile(r"^\s*module\s+([\w.-]+)", re.IGNORECASE | re.MULTILINE)
TABLE_CONTENT_PATTERN: Final[re.compile] = re.compile(r"=\s*\{(?P<content>[\s\S]*)\}")
TRANSLATION_LINE_PATTERN: Final[re.compile] = re.compile(r"^\s*([\w.-]+)\s*=\s*.+,?\s*$", re.MULTILINE)
TRANSLATION_VALUE_PATTERN: Final[re.compile] = re.compile(r"=\s*\"((?:[^\"\\]|\\.)*)\"", re.DOTALL)
ITEM_PATTERN: Final[re.compile] = re.compile(r"item\s+([\w-]+)\s*\{(.*?)\}", re.MULTILINE | re.IGNORECASE | re.DOTALL)
RECIPE_PATTERN: Final[re.compile] = re.compile(r"(?:recipe|craftRecipe)\s+(.*?)\s*\{", re.MULTILINE | re.IGNORECASE)
DISPLAY_NAME_PATTERN: Final[re.compile] = re.compile(r"DisplayName\s*=\s*(.*?)(?:,|\n|$)")
RECIPE_FORMAT_PATTERN_1: Final[re.compile] = re.compile(r'([a-z\d])([A-Z])')
RECIPE_FORMAT_PATTERN_2: Final[re.compile] = re.compile(r'([A-Z]+)([A-Z][a-z])')

class Config:
    def __init__(self):
        parser = configparser.ConfigParser()
        if not CONFIG_FILE.is_file():
            raise FileNotFoundError(f"错误：配置文件 '{CONFIG_FILE}' 不存在。请根据模板创建。")
        parser.read(CONFIG_FILE, encoding='utf-8')
        try:
            self.TARGET_PATH = Path(parser.get('Paths', 'target_path'))
            self.COMPLETED_PATH = Path(parser.get('Paths', 'completed_path'))
            self.OUTPUT_PARENT_PATH = Path(parser.get('Paths', 'output_parent_path'))
            self.PRIORITY_LANGUAGE = parser.get('Settings', 'priority_language')
            self.BASE_LANGUAGE = parser.get('Settings', 'base_language')
            self.TRANSLATION_FILE_EXT = parser.get('Settings', 'translation_file_ext')
            self.SCRIPTS_FILE_EXT = parser.get('Settings', 'scripts_file_ext')
            self.OUTPUT_FILENAME = parser.get('Output', 'output_filename')
            self.EN_TODO_FILENAME = parser.get('Output', 'en_todo_filename')
            self.COMPLETED_FILENAME = parser.get('Output', 'completed_filename')
            self.CN_ONLY_FILENAME = parser.get('Output', 'cn_only_filename')
            self.CN_OUTPUT_FILENAME = parser.get('Output', 'cn_output_filename')
            self.EN_OUTPUT_FILENAME = parser.get('Output', 'en_output_filename')
            self.LOG_FILENAME_TPL = parser.get('Output', 'log_filename_tpl')
            self.UPDATE_LOG_FILENAME = parser.get('Output', 'update_log_filename')
            self.ITEM_PREFIX_TPL = parser.get('Prefixes', 'item_prefix_tpl')
            self.RECIPE_PREFIX = parser.get('Prefixes', 'recipe_prefix')
        except (configparser.NoSectionError, configparser.NoOptionError) as e:
            raise ValueError(f"错误：配置文件 '{CONFIG_FILE}' 中缺少必要的配置项: {e}")

def get_old_file_content(file_path: Path) -> str | None:
    git_path = file_path.as_posix()
    command = ["git", "show", f"HEAD:{git_path}"]
    
    logging.info(f"    -> 正在尝试从 Git 历史记录中获取旧版本: {git_path}")
    
    try:
        result = subprocess.run(
            command,
            capture_output=True,
            text=True,
            check=False
        )
        
        if result.returncode == 0:
            logging.info("      --> 成功找到旧版本。")
            return result.stdout
        else:
            logging.info("      --> 在 Git 历史记录中未找到该文件，视为全新。")
            return None
            
    except FileNotFoundError:
        logging.warning("    -> 警告: 'git' 命令未找到。无法执行差异化日志记录。")
        return None
    except Exception as e:
        logging.error(f"    -> 获取旧文件时发生未知错误: {e}")
        return None


def find_case_insensitive_dir(parent_path, target_dir_name):
    if not parent_path or not parent_path.is_dir(): return None
    for entry in parent_path.iterdir():
        if entry.is_dir() and entry.name.lower() == str(target_dir_name).lower():
            return entry
    return None

def find_versioned_dir(parent_path):
    if not parent_path or not parent_path.is_dir(): return None
    version_dirs = [d for d in parent_path.iterdir() if d.is_dir() and VERSION_DIR_PATTERN.match(d.name)]
    if not version_dirs: return None
    highest_version_dir = sorted(version_dirs, key=lambda v: tuple(map(int, v.name.split('.'))), reverse=True)[0]
    return highest_version_dir

def find_active_media_path(mod_root_path):
    logging.info(f"\n--- 正在为 '{mod_root_path.name}' 动态查找 'media' 文件夹 ---")
    version_dir = find_versioned_dir(mod_root_path)
    if version_dir:
        logging.info(f"  -> 发现版本目录: {version_dir.name}")

    common_dir = find_case_insensitive_dir(mod_root_path, 'common')

    potential_media_paths = [
        find_case_insensitive_dir(version_dir, 'media') if version_dir else None,
        find_case_insensitive_dir(mod_root_path, 'media'),
        find_case_insensitive_dir(common_dir, 'media') if common_dir else None
    ]

    for media_path in potential_media_paths:
        if media_path and media_path.is_dir():
            logging.info(f"  -> 正在检查路径的有效性: {media_path}")

            has_scripts = find_case_insensitive_dir(media_path, "scripts")
            has_translate = find_case_insensitive_dir(media_path / "lua" / "shared", "Translate")

            if has_scripts or has_translate:
                logging.info(f"  --> 路径有效！将使用此路径进行处理: {media_path}")
                return media_path
            else:
                logging.info(f"  --> 路径无效 (缺少 scripts 或 Translate)，继续查找...")

    logging.warning("  --> 未能在任何优先路径中找到 'media' 文件夹。")
    return None

def extract_item_display_names(text_content, prefix):
    results = {}
    for item_match in ITEM_PATTERN.finditer(text_content):
        item_name, item_content = item_match.groups()
        display_name_match = DISPLAY_NAME_PATTERN.search(item_content)
        if display_name_match:
            display_name_raw = display_name_match.group(1).strip()
            display_name_escaped = display_name_raw.replace('"', '\\"')
            key = f'{prefix}.{item_name}'; line = f'{key} = "{display_name_escaped}",'
            results[key] = line
    return results

def format_recipe_name(name):
    parts = name.split('.'); formatted_parts = []
    for part in parts:
        s1 = RECIPE_FORMAT_PATTERN_1.sub(r'\1 \2', part)
        s2 = RECIPE_FORMAT_PATTERN_2.sub(r'\1 \2', s1)
        formatted_parts.append(s2)
    return ". ".join(formatted_parts)

def extract_recipe_names(text_content, config):
    results = {}
    for recipe_match in RECIPE_PATTERN.finditer(text_content):
        original_name = recipe_match.group(1).strip()
        friendly_name = format_recipe_name(original_name)
        modified_name = original_name.replace(' ', '_')
        key = f"{config.RECIPE_PREFIX}_{modified_name}"; line = f'{key} = "{friendly_name}",'
        results[key] = line
    return results

def get_translations_as_dict(file_path_or_dir, config):
    if isinstance(file_path_or_dir, Path) and file_path_or_dir.is_dir():
        all_translations = {}
        logging.info(f"  -> 扫描目录: {file_path_or_dir}")
        for file_path in sorted(file_path_or_dir.glob(f"*{config.TRANSLATION_FILE_EXT}")):
            all_translations.update(get_translations_as_dict(file_path, config))
        return all_translations
    translations_dict = {}
    if not file_path_or_dir:
        return translations_dict

    if not file_path_or_dir.is_file():
        logging.info(f"  -> 文件 '{file_path_or_dir.name}' 在目标位置不存在，将自动创建。")
        try:
            file_path_or_dir.parent.mkdir(parents=True, exist_ok=True)
            file_path_or_dir.write_text("", encoding='utf-8')
        except Exception as e:
            logging.error(f"  -> 错误：自动创建文件 '{file_path_or_dir}' 失败: {e}")        
        return translations_dict
    
    keys_found_in_file = 0
    try:
        content = file_path_or_dir.read_text(encoding='utf-8')
        table_content_match = TABLE_CONTENT_PATTERN.search(content)
        content_to_parse = table_content_match.group('content') if table_content_match else content
        for match in TRANSLATION_LINE_PATTERN.finditer(content_to_parse):
            key, line = match.group(1).strip(), match.group(0).strip()
            if line.endswith("= {"): continue
            if not line.endswith(','): line += ","
            translations_dict[key] = line
            keys_found_in_file += 1
    except Exception as e:
        logging.error(f"    处理文件 {file_path_or_dir.name} 时发生错误: {e}")
    logging.info(f"     -> 在 '{file_path_or_dir.name}' 中找到 {keys_found_in_file} 个键。")
    return translations_dict

def extract_value_from_line(line):
    match = TRANSLATION_VALUE_PATTERN.search(line)
    return match.group(1) if match else None

def process_single_mod(mod_root_path, config):
    active_media_path = find_active_media_path(mod_root_path)
    if not active_media_path:
        return {}, {}
    scripts_dir = find_case_insensitive_dir(active_media_path, "scripts")
    translate_root_dir = find_case_insensitive_dir(active_media_path / "lua" / "shared", "Translate")
    base_lang_dir = find_case_insensitive_dir(translate_root_dir, config.BASE_LANGUAGE)
    priority_lang_dir = find_case_insensitive_dir(translate_root_dir, config.PRIORITY_LANGUAGE)

    logging.info(f"\n--- 预加载 {config.BASE_LANGUAGE} (L1) 数据 ---")
    en_data_raw = get_translations_as_dict(base_lang_dir, config)
    local_known_en_keys = set(en_data_raw.keys())
    logging.info(f"预加载完成: 找到 {len(local_known_en_keys)} 个本地 {config.BASE_LANGUAGE} 键。")

    logging.info(f"\n--- 阶段 1: 扫描 Scripts (L0) ---")
    generated_data = {}
    if scripts_dir and scripts_dir.is_dir():
        for file_path in sorted(scripts_dir.rglob(f"*{config.SCRIPTS_FILE_EXT}")):
            logging.info(f"  -> 处理: {file_path.relative_to(scripts_dir)}")
            new_items, new_recipes = 0, 0
            try:
                content = file_path.read_text(encoding='utf-8')
                module_match = MODULE_PATTERN.search(content)
                module_name = module_match.group(1).strip() if module_match else "Base"
                item_prefix = config.ITEM_PREFIX_TPL.format(module_name=module_name)
                for key, line in extract_item_display_names(content, item_prefix).items():
                    if key not in local_known_en_keys: generated_data[key] = line; new_items += 1
                for key, line in extract_recipe_names(content, config).items():
                    if key not in local_known_en_keys: generated_data[key] = line; new_recipes += 1
            except Exception as e: logging.error(f"    处理文件 {file_path.name} 时发生错误: {e}")
            if new_items or new_recipes:
                log_parts = []
                if new_items: log_parts.append(f"{new_items} 个 Item")
                if new_recipes: log_parts.append(f"{new_recipes} 个 Recipe")
                logging.info(f"     -> 新增: " + ", ".join(log_parts))
    else: logging.warning(f"  -> 警告：未找到 Scripts 目录，跳过。")
    logging.info(f"阶段 1 完成: 从 scripts 新生成了 {len(generated_data)} 条数据。")
    
    en_base_data = {**generated_data, **en_data_raw}
    logging.info(f"\n--- 阶段 2: 合并 L0 与 L1 后，纯净英文基准总计: {len(en_base_data)} 条数据。---")

    cn_base_data = get_translations_as_dict(priority_lang_dir, config)
    logging.info(f"\n--- 阶段 3: 从 {config.PRIORITY_LANGUAGE} 目录加载了 {len(cn_base_data)} 条数据。---")
    
    return en_base_data, cn_base_data

def setup_logger(log_file_path):
    for handler in logging.root.handlers[:]:
        logging.root.removeHandler(handler)
    logging.basicConfig(level=logging.INFO, format='%(message)s',
        handlers=[logging.FileHandler(log_file_path, mode='w', encoding='utf-8'), logging.StreamHandler()])

def write_output_file(path, data):
    path.write_text("\n".join(data[k] for k in sorted(data.keys())), encoding='utf-8')

def get_file_last_commit_sha(file_path: Path) -> str | None:
    """获取指定文件的最新一次提交的SHA。"""
    if not file_path.is_file():
        return None
    command = ["git", "log", "-n", "1", "--pretty=format:%H", "--", file_path.as_posix()]
    try:
        result = subprocess.run(
            command, capture_output=True, text=True, check=True, encoding='utf-8'
        )
        return result.stdout.strip()
    except (subprocess.CalledProcessError, FileNotFoundError):
        return None

# (修复2 - 步骤A) 重构函数，使其在内存中更新状态字典，而不是直接写入文件
def record_completed_sha_in_memory(status_data: dict, mod_id: str, completed_file_path: Path) -> dict:
    """
    在内存中的状态字典里，记录指定Mod的已完成文件的最新Commit SHA。
    返回更新后的字典。
    """
    current_sha = get_file_last_commit_sha(completed_file_path)
    
    if current_sha:
        if mod_id not in status_data:
            status_data[mod_id] = {}
        status_data[mod_id]['completed_file_sha'] = current_sha
        logging.info(f"    -> [内存] 已为 Mod {mod_id} 记录 Commit SHA: {current_sha[:7]}")
    else:
        logging.warning(f"    -> 警告：未能获取 Mod {mod_id} 的完成文件SHA，状态未记录。")
        
    return status_data

def load_status():
    if STATUS_FILE.is_file():
        try:
            return json.loads(STATUS_FILE.read_text(encoding='utf-8'))
        except json.JSONDecodeError:
            return {}
    return {}

def save_status(status_data):
    STATUS_FILE.parent.mkdir(parents=True, exist_ok=True)
    STATUS_FILE.write_text(json.dumps(status_data, indent=2), encoding='utf-8')

def main():
    try:
        cfg = Config()
    except (FileNotFoundError, ValueError) as e:
        logging.error(e)
        return

    if not cfg.TARGET_PATH.is_dir():
        logging.error(f"错误：指定的目标路径不存在: {cfg.TARGET_PATH}")
        return

    run_id = f"run_{datetime.now().strftime('%Y%m%d_%H%M%S')}"
    logging.info(f"--- 本次运行ID: {run_id} ---")

    run_status = load_status()
    update_log_entries = []

    completed_path = cfg.COMPLETED_PATH
    completed_path.mkdir(exist_ok=True)
    
    mods_to_process = []
    if len(sys.argv) > 1 and sys.argv[1]:
        try:
            manual_ids_str = sys.argv[1]
            mod_ids_to_process = json.loads(manual_ids_str)
            if not isinstance(mod_ids_to_process, list):
                raise ValueError("传入的参数不是一个有效的JSON列表。")
                
            logging.info(f"手动触发模式: 收到 {len(mod_ids_to_process)} 个待处理的Mod ID。")
            
            for mod_id in sorted(mod_ids_to_process):
                mod_id_path = cfg.TARGET_PATH / str(mod_id)
                if mod_id_path.is_dir():
                    mods_to_process.append(mod_id_path)
                else:
                    logging.warning(f"\n警告：在 {cfg.TARGET_PATH} 中未找到手动传入的 ID {mod_id} 文件夹，已跳过。")
        except (json.JSONDecodeError, ValueError) as e:
            logging.error(f"错误：解析手动传入的Mod ID列表时失败: {e}")
            logging.error(f"收到的原始参数: {sys.argv[1]}")
            return
    else:
        logging.info("自动/计划模式: 从 id_list.txt 加载Mod列表。")
        if cfg.TARGET_PATH.name == 'mods':
            mods_to_process.append(cfg.TARGET_PATH.parent)
        else:
            try:
                lines = ID_LIST_FILE.read_text(encoding='utf-8').splitlines()
                mod_ids_to_process = {line.strip() for line in lines if line.strip().isdigit()}
                logging.info(f"成功加载 {ID_LIST_FILE} ,将处理 {len(mod_ids_to_process)} 个Mod。")
                for mod_id in sorted(list(mod_ids_to_process)):
                    mod_id_path = cfg.TARGET_PATH / mod_id
                    if mod_id_path.is_dir():
                        mods_to_process.append(mod_id_path)
                    else:
                        logging.warning(f"\n警告：在 {cfg.TARGET_PATH} 中未找到 ID 为 {mod_id} 的文件夹，已跳过。")
            except FileNotFoundError:
                logging.error(f"错误：未找到 {ID_LIST_FILE} 文件。请在列表模式下提供此文件。")
                return

    for mod_id_path in mods_to_process:
        mods_parent_path = mod_id_path / "mods"
        if not mods_parent_path.is_dir(): continue
        sub_mods = sorted([d for d in mods_parent_path.iterdir() if d.is_dir()])
        if not sub_mods: continue
        
        main_mod_name = sub_mods[0].name.replace(" ", "_")
        mod_id = mod_id_path.name
        output_dir_name = f"{main_mod_name}_{mod_id}"
        
        output_parent = cfg.OUTPUT_PARENT_PATH
        output_parent.mkdir(exist_ok=True)
        output_dir = output_parent / output_dir_name
        output_dir.mkdir(exist_ok=True)
        logs_dir = output_parent / "logs"
        logs_dir.mkdir(exist_ok=True)
        
        log_filename = cfg.LOG_FILENAME_TPL.format(mod_name=main_mod_name, mod_id=mod_id)
        setup_logger(logs_dir / log_filename)
        
        logging.info(f"\n\n{'='*25} 开始处理 Workshop ID: {mod_id} ({main_mod_name}) {'='*25}")
        
        completed_mod_path = completed_path / mod_id
        completed_mod_path.mkdir(exist_ok=True) 
        completed_todo_file = completed_mod_path / cfg.COMPLETED_FILENAME
        logging.info(f"\n--- 正在检查已完成的翻译于: {completed_todo_file} ---")
        completed_todo_data = get_translations_as_dict(completed_todo_file, cfg)
        completed_keys = set(completed_todo_data.keys())

        workshop_en_base, workshop_cn_base = {}, {}
        global_known_keys_en, global_known_keys_cn = set(), set()

        for sub_mod_path in sub_mods:
            logging.info(f"\n-------------------- 处理子模组: {sub_mod_path.name} --------------------")
            en_raw, cn_raw = process_single_mod(sub_mod_path, cfg)
            for key, val in en_raw.items():
                if key not in global_known_keys_en: workshop_en_base[key] = val
            for key, val in cn_raw.items():
                if key not in global_known_keys_cn: workshop_cn_base[key] = val
            global_known_keys_en.update(en_raw.keys())
            global_known_keys_cn.update(cn_raw.keys())
        
        final_output = {**workshop_en_base, **workshop_cn_base}
        en_todo_list, cn_only_list = {}, {}
        en_keys, cn_keys = set(workshop_en_base.keys()), set(workshop_cn_base.keys())
        current_todo_list = {}
        for key, en_line in workshop_en_base.items():
            if key in cn_keys:
                cn_line = workshop_cn_base[key]
                en_val, cn_val = extract_value_from_line(en_line), extract_value_from_line(cn_line)
                if en_val is not None and en_val == cn_val:
                    current_todo_list[key] = en_line
            else:
                current_todo_list[key] = en_line
        for key, line in current_todo_list.items():
            if key not in completed_keys:
                en_todo_list[key] = line
        for key, cn_line in workshop_cn_base.items():
            if key not in en_keys:
                cn_only_list[key] = cn_line

        logging.info(f"\n--- 正在为 Mod '{main_mod_name}' 生成输出文件 ---")
        logging.info(f"    - 最终合并 (output.txt): {len(final_output)} 条")
        logging.info(f"    - 纯净英文 (EN_output.txt): {len(workshop_en_base)} 条")
        logging.info(f"    - 纯净中文 (CN_output.txt): {len(workshop_cn_base)} 条")
        logging.info(f"    - 英文待办 (en_todo.txt): {len(en_todo_list)} 条 (增量)")
        logging.info(f"    - 中文独有 (cn_only.txt): {len(cn_only_list)} 条 (增量)")
        
        try:
            write_output_file(output_dir / cfg.OUTPUT_FILENAME, final_output)
            write_output_file(output_dir / cfg.EN_OUTPUT_FILENAME, workshop_en_base)
            write_output_file(output_dir / cfg.CN_OUTPUT_FILENAME, workshop_cn_base)
            write_output_file(output_dir / cfg.EN_TODO_FILENAME, en_todo_list)
            write_output_file(output_dir / cfg.CN_ONLY_FILENAME, cn_only_list)
            write_output_file(completed_mod_path / cfg.EN_TODO_FILENAME, en_todo_list)
            
            new_todo_file_path = output_dir / cfg.EN_TODO_FILENAME
            old_todo_content = get_old_file_content(new_todo_file_path)
            
            log_archive_dir = Path('data/logs/archive')
            log_archive_dir.mkdir(parents=True, exist_ok=True)

            timestamp = datetime.now().isoformat()

            if old_todo_content is None:
                if en_todo_list:
                    baseline_log_entry = {
                        "run_id": run_id,
                        "timestamp": timestamp,
                        "mod_name": main_mod_name,
                        "mod_id": mod_id,
                        "status": "baseline",
                        "added_count": len(en_todo_list),
                        "added_keys": sorted(list(en_todo_list.keys()))
                    }
                    archive_file = log_archive_dir / f"{main_mod_name}_{mod_id}_baseline_{run_id}.json"
                    with open(archive_file, 'w', encoding='utf-8') as f:
                        json.dump(baseline_log_entry, f, ensure_ascii=False, indent=2)
                    logging.info(f"    -> 基线日志已存档到: {archive_file}")
            else:
                new_keys = set(en_todo_list.keys())
                old_keys = set()
                try:
                    class StringPath:
                        def __init__(self, content, name):
                            self.content = content
                            self.name = name
                        def read_text(self, encoding='utf-8'): return self.content
                        def is_file(self): return True
                        @property
                        def parent(self): return Path('.')

                    old_todo_stream_obj = StringPath(old_todo_content, f"{cfg.EN_TODO_FILENAME} (旧版本)")
                    old_todo_data = get_translations_as_dict(old_todo_stream_obj, cfg)
                    old_keys = set(old_todo_data.keys())
                except Exception as e:
                    logging.warning(f"解析旧版 todo 文件内容时出错: {e}。将视为全新文件处理。")
                
                added_keys = new_keys - old_keys
                removed_keys = old_keys - new_keys

                if added_keys or removed_keys:
                    update_log_entry = {
                        "run_id": run_id,
                        "timestamp": timestamp,
                        "mod_name": main_mod_name,
                        "mod_id": mod_id,
                        "status": "updated",
                        "added_count": len(added_keys),
                        "removed_count": len(removed_keys),
                        "added_keys": sorted(list(added_keys)),
                        "removed_keys": sorted(list(removed_keys))
                    }
                    update_log_entries.append(update_log_entry)
                    logging.info(f"    -> 检测到内容变更。新增: {len(added_keys)}, 移除: {len(removed_keys)}")

            logging.info(f"\n处理成功！所有输出文件已保存在 '{output_dir_name}' 文件夹中。")
            run_status = record_completed_sha_in_memory(run_status, mod_id, completed_todo_file)

        except PermissionError:
            logging.error(f"\n错误：权限不足，无法写入文件到 '{output_dir}'。请检查文件夹权限。")
        except Exception as e:
            logging.error(f"写入输出文件时发生致命错误: {e}")

    logging.info("\n--- 所有模组处理循环结束，正在保存最终运行状态 ---")
    save_status(run_status)
    
    if update_log_entries:
        log_dir = Path('data/logs')
        log_dir.mkdir(parents=True, exist_ok=True)
        update_log_json_path = log_dir / 'update_log.json'
        
        existing_logs = []
        if update_log_json_path.is_file():
            try:
                with open(update_log_json_path, 'r', encoding='utf-8') as f:
                    existing_logs = json.load(f)
            except (json.JSONDecodeError, FileNotFoundError):
                logging.warning(f"无法解析现有的 {update_log_json_path}，将创建一个新的。")

        existing_logs.extend(update_log_entries)
        
        with open(update_log_json_path, 'w', encoding='utf-8') as f:
            json.dump(existing_logs, f, ensure_ascii=False, indent=2)
        logging.info(f"\n增量更新日志已记录到: {update_log_json_path}")

    logging.info("\n--- 所有模组处理完毕，开始生成状态报告 ---")
    try:
        report_script_path = Path('scripts/generate_status.py')
        if report_script_path.is_file():
            result = subprocess.run(
                ['python', str(report_script_path)],
                capture_output=True, text=True, check=True, encoding='utf-8'
            )
            logging.info("状态报告生成脚本输出:\n" + result.stdout)
            if result.stderr:
                logging.warning("状态报告生成脚本错误输出:\n" + result.stderr)
        else:
            logging.warning(f"未找到报告生成脚本: {report_script_path}")
    except FileNotFoundError:
        logging.error("错误: 'python' 命令未找到。无法执行报告生成脚本。")
    except subprocess.CalledProcessError as e:
        logging.error(f"执行报告生成脚本失败: {e}")
        logging.error("脚本输出:\n" + e.stdout)
        logging.error("脚本错误输出:\n" + e.stderr)
    except Exception as e:
        logging.error(f"生成报告时发生未知错误: {e}")

if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO, format='%(message)s')
    main()
