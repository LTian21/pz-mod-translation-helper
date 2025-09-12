import json
import sys
import configparser
import math

def main():
    """
    Calculates the number of mods per job based on the total number of mods and a maximum number of jobs.
    """
    try:
        if len(sys.argv) != 2:
            raise IndexError("需要1个参数 (mod_ids_json_string)，但收到了 " + str(len(sys.argv) - 1))

        mod_ids_json_string = sys.argv[1]
        mod_ids = json.loads(mod_ids_json_string)
        total_mods = len(mod_ids)

        if total_mods == 0:
            print(1) # 如果没有mod，默认返回1，避免除零错误
            return

        config = configparser.ConfigParser()
        config.read('config.ini')
        
        # 从config.ini读取max_jobs，如果找不到则默认为20
        max_jobs = config.getint('Workflow', 'max_jobs', fallback=16)

        # 计算每个job需要处理的mod数量，确保job总数不超过max_jobs
        # 使用math.ceil确保所有mod都能被分配
        mods_per_job = math.ceil(total_mods / max_jobs)
        
        # 确保mods_per_job至少为1
        if mods_per_job < 1:
            mods_per_job = 1

        print(int(mods_per_job))

    except (IndexError, ValueError, json.JSONDecodeError, configparser.Error) as e:
        print(f"错误: 脚本执行失败 - {e}", file=sys.stderr)
        # 如果出错，则回退到一个安全的默认值10
        print(10)
        sys.exit(1)

if __name__ == "__main__":
    main()
