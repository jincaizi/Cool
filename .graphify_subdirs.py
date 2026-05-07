import json
from pathlib import Path

d = json.load(open('E:/CodeForJob/Cool/.graphify_detect.json'))

# Only consider first-class project directories under Assets/
# Third-party should be excluded
EXCLUDE = {'PackageCache', 'Plugins', 'fantasySpider', 'Monstor'}

def is_project_file(f):
    p = Path(f)
    parts = p.parts
    for i, part in enumerate(parts):
        if part == 'Assets' and i + 1 < len(parts):
            sub = parts[i+1]
            if sub in EXCLUDE:
                return False
            return True
    return False

def top_dir(f):
    p = Path(f)
    parts = p.parts
    for i, part in enumerate(parts):
        if part == 'Assets':
            return '/'.join(parts[i:i+3])
    return None

# Code by top-level project dir
code_files = d['files'].get('code', [])
project_code = [f for f in code_files if is_project_file(f)]
by_top = {}
for f in project_code:
    td = top_dir(f)
    if td:
        by_top[td] = by_top.get(td, 0) + 1

# Docs by project dir
doc_files = d['files'].get('document', [])
project_docs = [f for f in doc_files if is_project_file(f)]
by_top_docs = {}
for f in project_docs:
    td = top_dir(f)
    if td:
        by_top_docs[td] = by_top_docs.get(td, 0) + 1

# Images by project dir
img_files = d['files'].get('image', [])
project_imgs = [f for f in img_files if is_project_file(f)]
by_top_imgs = {}
for f in project_imgs:
    td = top_dir(f)
    if td:
        by_top_imgs[td] = by_top_imgs.get(td, 0) + 1

print("Top-level project directories with counts:")
all_tops = set(by_top.keys()) | set(by_top_docs.keys()) | set(by_top_imgs.keys())
rows = []
for td in sorted(all_tops):
    c = by_top.get(td, 0)
    docs = by_top_docs.get(td, 0)
    imgs = by_top_imgs.get(td, 0)
    total = c + docs + imgs
    rows.append((td, c, docs, imgs, total))

rows.sort(key=lambda x: -x[4])
print(f"\n{'Dir':<50} {'Code':>6} {'Docs':>6} {'Images':>8} {'Total':>6}")
print("-" * 80)
for td, c, docs, imgs, total in rows:
    print(f"{td:<50} {c:>6} {docs:>6} {imgs:>8} {total:>6}")