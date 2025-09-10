import configparser
import json
import math
from pathlib import Path

import sys

def generate_matrix():
    """
    Generates a JSON string for GitHub Actions Matrix based on a given list of mod IDs and configuration.

    This script receives a JSON array of mod IDs as a command-line argument,
    divides them into batches according to settings in config.ini,
    and prints a JSON object suitable for use in a GitHub Actions `strategy: matrix`.
    """
    config_file = Path('config.ini')

    # Read configuration
    parser = configparser.ConfigParser()
    if not config_file.is_file():
        raise FileNotFoundError(f"Error: Configuration file '{config_file}' not found.")
    parser.read(config_file, encoding='utf-8')

    try:
        mods_per_job = parser.getint('Settings', 'mods_per_job', fallback=10)
        max_concurrent_jobs = parser.getint('Settings', 'max_concurrent_jobs', fallback=16)
    except (configparser.NoSectionError, configparser.NoOptionError) as e:
        raise ValueError(f"Error: Missing required settings in '{config_file}': {e}")

    # Read mod IDs from command-line argument
    if len(sys.argv) < 2:
        print(json.dumps({"include": []}), file=sys.stderr)
        return

    try:
        mod_ids = json.loads(sys.argv[1])
        if not isinstance(mod_ids, list):
            raise json.JSONDecodeError("Input is not a JSON list.", sys.argv, 0)
    except json.JSONDecodeError:
        print(f"Error: Invalid JSON array provided as an argument: {sys.argv}", file=sys.stderr)
        print(json.dumps({"include": []}))
        return

    total_mods = len(mod_ids)
    if total_mods == 0:
        print(json.dumps({"include": []}))
        return

    # Calculate batch size
    ideal_jobs = math.ceil(total_mods / mods_per_job)
    
    if ideal_jobs > max_concurrent_jobs:
        batch_size = math.ceil(total_mods / max_concurrent_jobs)
    else:
        batch_size = mods_per_job

    # Create batches
    matrix = {"include": []}
    for i in range(0, total_mods, batch_size):
        batch = mod_ids[i:i+batch_size]
        matrix["include"].append({
            "job_id": i // batch_size,
            "mod_ids": ",".join(batch)
        })

    # Print JSON output for GitHub Actions
    print(json.dumps(matrix))

if __name__ == "__main__":
    generate_matrix()
