import json
import os
import sys

def main():
    try:
        json_string = sys.argv[1]
        group_size = int(sys.argv[2])
    except (IndexError, ValueError):
        print("Usage: python split_ids.py '<json_array_string>' <group_size>")
        sys.exit(1)

    id_list = json.loads(json_string)
    if not id_list:
        print("[]")
        return

    groups = [id_list[i:i + group_size] for i in range(0, len(id_list), group_size)]
    print(json.dumps(groups))

if __name__ == "__main__":
    main()