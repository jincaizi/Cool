import json
import glob
from pathlib import Path

chunks = sorted(glob.glob('E:/CodeForJob/Cool/graphify-out/.graphify_chunk_*.json'))
print(f"Checking {len(chunks)} chunks...")

valid_chunks = []
for c in chunks:
    try:
        with open(c, 'r', encoding='utf-8') as f:
            content = f.read()
        # Fix common JSON issues
        # 1. Missing comma before "hyperedges"
        if '"hyperedges":[]' in content:
            # Check if it's ]],"hyperedges" (double closing bracket)
            pass
        d = json.loads(content)
        valid_chunks.append(c)
        print(f"OK: {c} ({len(d.get('nodes',[]))} nodes, {len(d.get('edges',[]))} edges)")
    except json.JSONDecodeError as e:
        print(f"FAIL: {c} - {e}")
        # Try to fix: check for double ]] before hyperedges
        with open(c, 'r', encoding='utf-8') as f:
            content = f.read()
        # If we see ]],"hyperedges" - the ]] has an extra ]
        if ']],"hyperedges"' in content:
            fixed = content.replace(']],"hyperedges"', '],"hyperedges"')
            try:
                d = json.loads(fixed)
                with open(c, 'w', encoding='utf-8') as f2:
                    f2.write(fixed)
                valid_chunks.append(c)
                print(f"  FIXED: removed extra ] from {c}")
            except:
                pass

print(f"\nValid chunks: {len(valid_chunks)}")

all_nodes, all_edges, all_hyperedges = [], [], []
for c in valid_chunks:
    with open(c, 'r', encoding='utf-8') as f:
        d = json.load(f)
    all_nodes += d.get('nodes', [])
    all_edges += d.get('edges', [])
    all_hyperedges += d.get('hyperedges', [])

print(f"Total: {len(all_nodes)} nodes, {len(all_edges)} edges, {len(all_hyperedges)} hyperedges")

Path('E:/CodeForJob/Cool/.graphify_semantic.json').write_text(json.dumps({
    'nodes': all_nodes, 'edges': all_edges, 'hyperedges': all_hyperedges,
    'input_tokens': 0, 'output_tokens': 0,
}, indent=2))
print("Saved .graphify_semantic.json")