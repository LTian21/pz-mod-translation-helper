import json
import os
import re
from datetime import datetime, timezone, timedelta
from pathlib import Path

# --- 配置 ---
COMPLETED_FILES_DIR = Path('data/output_files')
LOG_DIR = Path('data/logs')
UPDATE_LOG_JSON = LOG_DIR / 'update_log.json'

# --- 模板 ---
STATUS_TEMPLATE = """# 汉化中心状态仪表盘

![最后运行状态](https://img.shields.io/badge/Last%20Run-Success-green)
*最后更新于：{update_time}*

---

### 📈 **核心指标**

| 指标 | 状态 |
| :--- | :--- |
| **当前待办总数** | `{total_todos}` 条 |
| **已支持 Mod 数量** | `{mod_count}` 个 |

---

### ⚡ **最近一次更新摘要**

{summary_section}

---

> 详细的各 Mod 待办数量，请查看 [**Mod 待办状态**](MOD_TODO_STATUS.md)。
"""

INTERNAL_STATUS_TEMPLATE = """# 内部状态仪表盘

*此报告仅供内部使用，包含详细的调试和跟踪信息。*

![最后运行状态](https://img.shields.io/badge/Last%20Run-Success-green)
*最后更新于：{update_time}*

---

### 📈 **核心指标**

| 指标 | 状态 |
| :--- | :--- |
| **当前待办总数** | `{total_todos}` 条 |
| **已支持 Mod 数量** | `{mod_count}` 个 |

---

### ⚡ **最近一次运行详情 (Run ID: `{run_id}`)**

{detailed_summary_section}

---

> **日志文件**:
> *   [增量更新日志 (update_log.json)](../data/logs/update_log.json)
> *   [基线日志存档](../data/logs/archive/)
"""

MOD_TODO_STATUS_TEMPLATE = """# Mod 待办状态

*此页面展示了当前所有已支持 Mod 的待办翻译条目数量。*

*最后更新于：{update_time}*

---

| Mod 名称 | Mod ID | 待办条目数量 |
| :--- | :--- | :--- |
{mod_todo_table}
"""

def get_total_todo_lines(directory):
    """计算目录中所有 EN_todo.txt 文件的总行数。"""
    total_lines = 0
    if not directory.is_dir():
        return 0
    for mod_dir in directory.iterdir():
        if mod_dir.is_dir():
            todo_file = mod_dir / 'EN_todo.txt'
            if todo_file.is_file():
                try:
                    with open(todo_file, 'r', encoding='utf-8') as f:
                        lines = sum(1 for line in f if line.strip())
                        total_lines += lines
                except Exception as e:
                    print(f"Error reading {todo_file}: {e}")
    return total_lines

def get_supported_mod_count(directory):
    """计算已支持的 Mod 数量。"""
    if not directory.is_dir():
        return 0
    return len([name for name in directory.iterdir() if name.is_dir()])

def get_mod_todo_list(directory):
    """获取每个 Mod 的待办条目数量列表。"""
    mod_list = []
    if not directory.is_dir():
        return []
    for mod_dir in directory.iterdir():
        if mod_dir.is_dir():
            todo_file = mod_dir / 'EN_todo.txt'
            line_count = 0
            if todo_file.is_file():
                try:
                    with open(todo_file, 'r', encoding='utf-8') as f:
                        line_count = sum(1 for line in f if line.strip())
                except Exception as e:
                    print(f"Error reading {todo_file}: {e}")
            
            match = re.match(r'^(.*?)_(\d+)$', mod_dir.name)
            if match:
                mod_name, mod_id = match.groups()
                mod_list.append({'name': mod_name, 'id': mod_id, 'todos': line_count})
    
    # 按待办数量降序排序
    return sorted(mod_list, key=lambda x: x['todos'], reverse=True)

def get_latest_run_summary(log_file):
    """从 JSON 日志文件中获取最新一次运行的摘要。"""
    if not log_file.is_file():
        return "no_run_id", "*   *未找到更新日志。*", "*   *未找到更新日志。*"

    try:
        with open(log_file, 'r', encoding='utf-8') as f:
            logs = json.load(f)
    except (json.JSONDecodeError, FileNotFoundError):
        return "error", "*   *无法解析更新日志。*", "*   *无法解析更新日志。*"

    if not logs:
        return "no_logs", "*   *日志为空。*", "*   *日志为空。*"

    latest_run_id = logs[-1].get('run_id')
    
    run_logs = [log for log in logs if log.get('run_id') == latest_run_id]

    if not run_logs:
        return latest_run_id, "*   *最近一次运行没有内容变更。*", "*   *最近一次运行没有内容变更。*"

    # 生成公共摘要
    total_added = sum(log.get('added_count', 0) for log in run_logs)
    changed_mods_count = len(run_logs)
    
    summary_lines = [
        f"*   **新增待办翻译**: `{total_added}` 条",
        f"*   **内容变更的 Mod**: `{changed_mods_count}` 个"
    ]
    for i, log in enumerate(run_logs[:5]):
        summary_lines.append(f"    *   `{log['mod_name']} (ID: {log['mod_id']})`")
    if changed_mods_count > 5:
        summary_lines.append("    *   ... *等*")

    # 生成内部详细摘要
    detailed_summary_lines = []
    for log in run_logs:
        details = f"**{log['mod_name']} (ID: {log['mod_id']})**: "
        details += f"新增 `{log.get('added_count', 0)}` 条, "
        details += f"移除 `{log.get('removed_count', 0)}` 条。"
        detailed_summary_lines.append(f"*   {details}")

    return latest_run_id, "\n".join(summary_lines), "\n".join(detailed_summary_lines)


def main():
    """主函数，生成所有报告。"""
    print("--- 开始生成状态报告 ---")
    
    # 1. 收集通用数据
    beijing_time = datetime.now(timezone(timedelta(hours=8)))
    update_time_str = beijing_time.strftime('%Y-%m-%d %H:%M:%S %Z')
    
    total_todos = get_total_todo_lines(COMPLETED_FILES_DIR)
    mod_count = get_supported_mod_count(COMPLETED_FILES_DIR)
    mod_todo_list = get_mod_todo_list(COMPLETED_FILES_DIR)

    # 2. 从日志文件获取摘要
    run_id, summary, detailed_summary = get_latest_run_summary(UPDATE_LOG_JSON)

    # 3. 生成 STATUS.md
    status_md_content = STATUS_TEMPLATE.format(
        update_time=f"`{update_time_str}`",
        total_todos=f"`{total_todos}`",
        mod_count=f"`{mod_count}`",
        summary_section=summary
    )
    with open('STATUS.md', 'w', encoding='utf-8') as f:
        f.write(status_md_content)
    print("  -> STATUS.md 已生成。")

    # 4. 生成 INTERNAL_STATUS.md
    internal_status_md_content = INTERNAL_STATUS_TEMPLATE.format(
        update_time=f"`{update_time_str}`",
        total_todos=f"`{total_todos}`",
        mod_count=f"`{mod_count}`",
        run_id=f"`{run_id}`",
        detailed_summary_section=detailed_summary
    )
    with open('INTERNAL_STATUS.md', 'w', encoding='utf-8') as f:
        f.write(internal_status_md_content)
    print("  -> INTERNAL_STATUS.md 已生成。")

    # 5. 生成 MOD_TODO_STATUS.md
    mod_todo_table_rows = [
        f"| {mod['name']} | {mod['id']} | {mod['todos']} |"
        for mod in mod_todo_list
    ]
    mod_todo_status_content = MOD_TODO_STATUS_TEMPLATE.format(
        update_time=f"`{update_time_str}`",
        mod_todo_table="\n".join(mod_todo_table_rows)
    )
    with open('MOD_TODO_STATUS.md', 'w', encoding='utf-8') as f:
        f.write(mod_todo_status_content)
    print("  -> MOD_TODO_STATUS.md 已生成。")
    
    print("--- 所有报告生成完毕 ---")

if __name__ == '__main__':
    main()
