import json
from pathlib import Path

detect = json.load(open('E:/CodeForJob/Cool/.graphify_detect.json'))
all_files = []
for f in detect['files'].get('code', []):
    all_files.append(f)
for f in detect['files'].get('document', []):
    all_files.append(f)

# Make relative
base = Path('E:/CodeForJob/Cool/')
rel_files = [str(Path(f).relative_to(base)) for f in all_files]

print(f"Total files: {len(rel_files)}")
# Split into chunks of 22
chunk_size = 22
for i in range(0, len(rel_files), chunk_size):
    chunk = rel_files[i:i+chunk_size]
    print(f"\n--- CHUNK {i//chunk_size} (files {i+1}-{min(i+chunk_size, len(rel_files))}) ---")
    for f in chunk:
        print(f)