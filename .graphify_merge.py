import json
import glob
from pathlib import Path

chunks = sorted(glob.glob('E:/CodeForJob/Cool/graphify-out/.graphify_chunk_*.json'))
print(f"Merging {len(chunks)} chunks...")

all_nodes, all_edges, all_hyperedges = [], [], []
total_in, total_out = 0, 0

for c in chunks:
    with open(c, 'r', encoding='utf-8') as f:
        d = json.load(f)
    all_nodes += d.get('nodes', [])
    all_edges += d.get('edges', [])
    all_hyperedges += d.get('hyperedges', [])
    total_in += d.get('input_tokens', 0)
    total_out += d.get('output_tokens', 0)

print(f"Total: {len(all_nodes)} nodes, {len(all_edges)} edges, {len(all_hyperedges)} hyperedges")
print(f"Tokens: {total_in} in / {total_out} out")

Path('E:/CodeForJob/Cool/.graphify_semantic.json').write_text(json.dumps({
    'nodes': all_nodes, 'edges': all_edges, 'hyperedges': all_hyperedges,
    'input_tokens': total_in, 'output_tokens': total_out,
}, indent=2))
print("Saved .graphify_semantic.json")
