import json
from pathlib import Path

# Save manifest for --update
detect = {'files': {'code': [], 'document': []}}
base = Path('E:/CodeForJob/Cool/')
for f in Path('E:/CodeForJob/Cool/.graphify_detect.json').read_text().split('\n'):
    pass

# Re-read detect
detect2 = json.loads(Path('E:/CodeForJob/Cool/.graphify_detect.json').read_text())
all_files = []
for f in detect2['files'].get('code', []):
    all_files.append(f)
for f in detect2['files'].get('document', []):
    all_files.append(f)

from graphify.detect import save_manifest
save_manifest({f: True for f in all_files})

# Build graph
from graphify.build import build_from_json
from graphify.cluster import cluster, score_all
from graphify.analyze import god_nodes, surprising_connections, suggest_questions
from graphify.report import generate
from graphify.export import to_json
from pathlib import Path

extraction = json.loads(Path('E:/CodeForJob/Cool/.graphify_extract.json').read_text())
detection  = json.loads(Path('E:/CodeForJob/Cool/.graphify_detect.json').read_text())

G = build_from_json(extraction)
communities = cluster(G)
cohesion = score_all(G, communities)
tokens = {'input': extraction.get('input_tokens', 0), 'output': extraction.get('output_tokens', 0)}
gods = god_nodes(G)
surprises = surprising_connections(G, communities)
labels = {cid: 'Community ' + str(cid) for cid in communities}

report = generate(G, communities, cohesion, labels, gods, surprises, detection, tokens, 'Assets/Scripts')
Path('E:/CodeForJob/Cool/graphify-out/GRAPH_REPORT.md').write_text(report)
to_json(G, communities, 'E:/CodeForJob/Cool/graphify-out/graph.json')

analysis = {
    'communities': {str(k): v for k, v in communities.items()},
    'cohesion': {str(k): v for k, v in cohesion.items()},
    'gods': gods,
    'surprises': surprises,
}
Path('E:/CodeForJob/Cool/.graphify_analysis.json').write_text(json.dumps(analysis, indent=2))
if G.number_of_nodes() == 0:
    print('ERROR: Graph is empty')
    raise SystemExit(1)
print(f'Graph: {G.number_of_nodes()} nodes, {G.number_of_edges()} edges, {len(communities)} communities')
