import configparser
import json
import logging
import subprocess
from pathlib import Path

# 配置
logging.basicConfig(level=logging.INFO, format='%(message)s')
CONFIG_FILE = Path('config.ini')
STATUS_FILE = Path('data/.cache/.last_run_status.json')

def get_config():
    try:
        parser = configparser.ConfigParser()
        parser.read(CONFIG_FILE, encoding='utf-8')
        completed_path = Path(parser.get('Paths', 'completed_path'))
        completed_filename = parser.get('Output', 'completed_filename')
        return completed_path, completed_filename
    except Exception as e:
        logging.error(f"错误：读取配置文件 '{CONFIG_FILE}' 失败: {e}")
        return None, None

def load_status():
    if STATUS_FILE.is_file():
        try:
            return json.loads(STATUS_FILE.read_text(encoding='utf-8'))
        except json.JSONDecodeError:
            logging.warning(f"警告：状态文件 '{STATUS_FILE}' 格式错误，将视为空。")
            return {}
    return {}

def get_file_last_commit_sha(file_path: Path) -> str | None:
    if not file_path.is_file():
        return None
    command = ["git", "log", "-n", "1", "--pretty=format:%H", "--", str(file_path)]
    try:
        result = subprocess.run(
            command, capture_output=True, text=True, check=True, encoding='utf-8'
        )
        return result.stdout.strip()
    except subprocess.CalledProcessError:
        # 如果文件是新添加还未提交，git log会失败
        return None 
    except FileNotFoundError:
        logging.error("错误: 'git' 命令未找到。")
        return None

def main():
    completed_path, completed_filename = get_config()
    if not completed_path or not completed_path.is_dir():
        logging.info(f"'{completed_path}' 目录不存在，无需检查本地变更。")
        print(json.dumps([]))
        return

    run_status = load_status()
    changed_mod_ids = []

    logging.info(f"正在检查 '{completed_path}' 目录下的本地文件变更...")

    # 遍历 `completed` 目录下的所有Mod ID子目录
    for mod_dir in completed_path.iterdir():
        if not mod_dir.is_dir() or not mod_dir.name.isdigit():
            continue

        mod_id = mod_dir.name
        completed_file = mod_dir / completed_filename
        
        if not completed_file.is_file():
            continue

        current_sha = get_file_last_commit_sha(completed_file)
        if not current_sha:
            # 文件存在但无法获取SHA（例如，新添加未提交的文件），视为需要处理
            logging.info(f"  -> Mod {mod_id}: 发现新添加的完成文件，需要处理。")
            changed_mod_ids.append(mod_id)
            continue

        last_known_sha = run_status.get(mod_id, {}).get('completed_file_sha')

        if current_sha != last_known_sha:
            logging.info(
                f"  -> Mod {mod_id}: 检测到变更。"
                f" (记录SHA: {last_known_sha}, 当前SHA: {current_sha})"
            )
            changed_mod_ids.append(mod_id)

    if not changed_mod_ids:
        logging.info("  -> 未检测到需要重新处理的本地变更。")

    # 以JSON格式输出
    print(json.dumps(sorted(changed_mod_ids), ensure_ascii=False))

if __name__ == "__main__":
    main()