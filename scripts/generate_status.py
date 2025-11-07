import json
import os
import re
from datetime import datetime, timezone, timedelta
from pathlib import Path
from collections import defaultdict
import urllib.parse

# --- 配置 ---
TRANSLATIONS_FILE = Path('data/translations_CN.txt')
LOG_DIR = Path('data/logs')
MOD_ID_NAME_MAP = Path('translation_utils/mod_id_name_map.json')
UPDATE_LOG_JSON = LOG_DIR / 'update_log.json'
README_FILE = Path('README.md')

# --- 模板 ---
STATUS_TEMPLATE = """# 汉化中心状态仪表盘

![最后运行状态](https://img.shields.io/badge/Last%20Run-Success-green)
*最后更新于：{update_time}*

---

### 📈 **核心指标**

| 指标 | 状态 |
| :--- | :--- |
| **模组总条目** | `{total_entries}` 条 |
| **待翻译条目** | `{total_todos}` 条 |
| **已翻译条目** | `{total_translated}` 条 |
| **待校对条目** | `{total_to_proofread}` 条 |
| **已支持 Mod 数量** | `{mod_count}` 个 |

---

### ⚡ **最近一次运行详情 (Run ID: `{run_id}`)**

{detailed_summary_section}

---

### 📊 **各 Mod 翻译状态**

| Mod 名称 | Mod ID | 待翻译条目 | 待校对条目 | 缺少原文条目 | 模组总条目 |
| :--- | :--- | :--- | :--- | :--- | :--- |
{mod_todo_table}
"""

def parse_translation_file_stats(file_path, mod_id_name_map):
    """
    一次性遍历 translations_CN.txt 文件，计算所有需要的统计数据。
    """
    mod_stats = defaultdict(lambda: {
        'total_entries': 0, 
        'missing_en': 0, 
        'todo_keys': set(), 
        'to_proofread_keys': set()
    })
    
    if not file_path.is_file():
        print(f"错误: 翻译文件 '{file_path}' 未找到。")
        return {}, {}, 0

    with open(file_path, 'r', encoding='utf-8') as f:
        for line in f:
            match = re.search(r'(\d+)::(?:EN|CN)::([\w\.\-]+)', line)
            if not match:
                continue
            
            mod_id, key = match.groups()
            stats = mod_stats[mod_id]

            # 统计总条目和缺失原文 (仅计算 EN 行)
            if '::EN::' in line:
                stats['total_entries'] += 1
                if "======Original Text Missing====" in line:
                    stats['missing_en'] += 1
            
            # 统计待翻译和待校对
            if re.match(r'^\t\t', line):
                stats['todo_keys'].add(key)
            elif re.match(r'^\t(?!\t)', line):
                stats['to_proofread_keys'].add(key)

    # --- 后处理和格式化 ---
    
    # 计算全局指标
    global_stats = {
        'total_entries': sum(s['total_entries'] for s in mod_stats.values()),
        'total_todos': sum(len(s['todo_keys']) for s in mod_stats.values()),
        'total_to_proofread': sum(len(s['to_proofread_keys']) for s in mod_stats.values())
    }
    global_stats['total_translated'] = global_stats['total_entries'] - global_stats['total_todos']
    
    mod_count = len(mod_stats)

    # 格式化为用于表格的列表
    mod_list = []
    for mod_id, stats in mod_stats.items():
        mod_name = mod_id_name_map.get(mod_id, f"未知 Mod ({mod_id})")
        mod_list.append({
            'name': mod_name,
            'id': mod_id,
            'todos': len(stats['todo_keys']),
            'to_proofread': len(stats['to_proofread_keys']),
            'missing_en': stats['missing_en'],
            'total_entries': stats['total_entries']
        })
        
    # 按待办数量降序排序
    sorted_mod_list = sorted(mod_list, key=lambda x: x['todos'], reverse=True)
    
    return sorted_mod_list, global_stats, mod_count


def get_latest_run_summary(log_file):
    """从 JSON 日志文件中获取最新一次运行的摘要。"""
    if not log_file.is_file():
        return "no_run_id", "*   *未找到更新日志。*"

    try:
        with open(log_file, 'r', encoding='utf-8') as f:
            logs = json.load(f)
    except (json.JSONDecodeError, FileNotFoundError):
        return "error", "*   *无法解析更新日志。*"

    if not logs:
        return "no_logs", "*   *日志为空。*"

    latest_run_id = logs[-1].get('run_id')
    
    run_logs = [log for log in logs if log.get('run_id') == latest_run_id]

    if not run_logs:
        return latest_run_id, "*   *最近一次运行没有内容变更。*"

    # 生成内部详细摘要
    detailed_summary_lines = []
    for log in run_logs:
        details = f"**{log['mod_name']} (ID: {log['mod_id']})**: "
        details += f"新增 `{log.get('added_count', 0)}` 条, "
        details += f"移除 `{log.get('removed_count', 0)}` 条。"
        detailed_summary_lines.append(f"*   {details}")

    return latest_run_id, "\n".join(detailed_summary_lines)

def generate_badges(global_stats, mod_count):
    """根据全局统计数据生成 shields.io 徽章的 Markdown。"""
    total = global_stats['total_entries']
    translated = global_stats['total_translated']
    
    if total == 0:
        percentage = 0
    else:
        percentage = round((translated / total) * 100, 2)
    
    color = "red"
    if percentage > 70:
        color = "yellow"
    if percentage > 90:
        color = "green"
    if percentage > 99:
        color = "brightgreen"

    # URL 编码
    progress_text = urllib.parse.quote(f"翻译进度-{translated} / {total} ({percentage}%)")
    mods_text = urllib.parse.quote(f"支持模组-{mod_count} 个")
    
    badges_md = [
        f"![翻译进度](https://img.shields.io/badge/{progress_text}-{color})",
        f"![支持模组](https://img.shields.io/badge/{mods_text}-blue)"
    ]
    
    return "\n".join(badges_md)

def update_readme_badges(badges_md):
    """读取 README.md，找到占位符并插入徽章。"""
    if not README_FILE.is_file():
        print(f"警告: 未找到 {README_FILE}，跳过徽章更新。")
        return

    try:
        with open(README_FILE, 'r', encoding='utf-8') as f:
            readme_content = f.read()

        start_marker = "<!--START_STATUS_BADGES-->"
        end_marker = "<!--END_STATUS_BADGES-->"

        if start_marker in readme_content and end_marker in readme_content:
            # 构建新的内容块
            new_block = f"{start_marker}\n{badges_md}\n{end_marker}"
            
            # 使用正则表达式替换旧块
            pattern = re.compile(f"{re.escape(start_marker)}.*?{re.escape(end_marker)}", re.DOTALL)
            new_readme_content = pattern.sub(new_block, readme_content)

            if new_readme_content != readme_content:
                with open(README_FILE, 'w', encoding='utf-8') as f:
                    f.write(new_readme_content)
                print("  -> README.md 中的徽章已更新。")
            else:
                print("  -> README.md 中的徽章无需更新。")
        else:
            print("警告: 在 README.md 中未找到徽章占位符。")

    except Exception as e:
        print(f"错误: 更新 README.md 时出错: {e}")


def main():
    """主函数，生成所有报告。"""
    print("--- 开始生成状态报告 ---")
    
    # 1. 加载 Mod 名称映射
    if MOD_ID_NAME_MAP.is_file():
        with open(MOD_ID_NAME_MAP, 'r', encoding='utf-8') as f:
            mod_id_name_map = json.load(f)
    else:
        mod_id_name_map = {}
        print(f"警告: 在 {MOD_ID_NAME_MAP} 未找到 Mod ID 名称映射文件")

    # 2. 收集通用数据
    beijing_time = datetime.now(timezone(timedelta(hours=8)))
    update_time_str = beijing_time.strftime('%Y-%m-%d %H:%M:%S %Z')
    
    mod_todo_list, global_stats, mod_count = parse_translation_file_stats(TRANSLATIONS_FILE, mod_id_name_map)

    # 3. 从日志文件获取摘要
    run_id, detailed_summary = get_latest_run_summary(UPDATE_LOG_JSON)

    # 4. 生成 STATUS.md
    mod_todo_table_rows = [
        f"| {mod['name'].replace('|', '&#124;')} | {mod['id']} | {mod['todos']} | {mod['to_proofread']} | {mod['missing_en']} | {mod['total_entries']} |"
        for mod in mod_todo_list
    ]
    
    status_md_content = STATUS_TEMPLATE.format(
        update_time=f"`{update_time_str}`",
        total_entries=f"`{global_stats['total_entries']}`",
        total_todos=f"`{global_stats['total_todos']}`",
        total_translated=f"`{global_stats['total_translated']}`",
        total_to_proofread=f"`{global_stats['total_to_proofread']}`",
        mod_count=f"`{mod_count}`",
        run_id=f"`{run_id}`",
        detailed_summary_section=detailed_summary,
        mod_todo_table="\n".join(mod_todo_table_rows)
    )
    with open('STATUS.md', 'w', encoding='utf-8') as f:
        f.write(status_md_content)
    print("  -> STATUS.md 已生成。")
    
    # 5. 生成并更新 README.md 中的徽章
    badges_md = generate_badges(global_stats, mod_count)
    update_readme_badges(badges_md)
    
    print("--- 所有报告生成完毕 ---")

if __name__ == '__main__':
    main()
