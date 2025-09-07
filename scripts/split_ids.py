import json
import sys

def main():
    try:
        if len(sys.argv) != 3:
            raise IndexError("需要2个参数，但收到了 " + str(len(sys.argv) - 1))

        json_string = sys.argv[1]
        group_size = int(sys.argv[2])
        id_list = json.loads(json_string)
    except (IndexError, ValueError, json.JSONDecodeError) as e:
        print(f"错误: 参数处理失败 - {e}",file=sys.stderr)
        print("用法: python split_ids.py '<json_array_string>' <group_size>",file=sys.stderr)
        sys.exit(1)

    if not id_list:
        print("[]")
        return

    groups = [id_list[i:i + group_size] for i in range(0, len(id_list), group_size)]
    print(json.dumps(groups))

if __name__ == "__main__":
    main()