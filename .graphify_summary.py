import json
from pathlib import Path

d = json.load(open('E:/CodeForJob/Cool/.graphify_detect.json'))
code_files = d['files'].get('code', [])

# Find project-specific code (Assets/Scripts, Assets/Examples, not third-party)
project_roots = set()
for f in code_files:
    p = Path(f)
    parts = p.parts
    for i, part in enumerate(parts):
        if part == 'Assets' and i + 1 < len(parts):
            sub = parts[i+1]
            # Skip known third-party packages
            if sub not in ('PackageCache', 'Plugins', 'fantasySpider', 'Monstor'):
                project_roots.add(str(p.parent))

project_counts = {}
for f in code_files:
    p = Path(f)
    for root in project_roots:
        if str(p).startswith(root):
            project_counts[root] = project_counts.get(root, 0) + 1

print("Project source directories:")
for root, count in sorted(project_counts.items(), key=lambda x: -x[1])[:20]:
    print(f"  {count:3d}  {root}")

print(f"\nTotal project code files: {sum(project_counts.values())}")
print(f"Total code files: {len(code_files)}")
print(f"Total docs: {len(d['files'].get('document', []))}")
print(f"Total images: {len(d['files'].get('image', []))}")

# Show sample non-third-party project files
sample = [f for f in code_files if 'Assets/Scripts' in f][:10]
if sample:
    print("\nSample Assets/Scripts files:")
    for f in sample:
        print(f"  {f}")