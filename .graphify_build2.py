import json
from pathlib import Path

# Build graph
from graphify.build import build_from_json
from graphify.cluster import cluster, score_all
from graphify.analyze import god_nodes, surprising_connections, suggest_questions
from graphify.report import generate
from graphify.export import to_json

extraction = json.loads(Path('E:/CodeForJob/Cool/.graphify_extract.json').read_text())
detection  = json.loads(Path('E:/CodeForJob/Cool/.graphify_detect.json').read_text())

G = build_from_json(extraction)
print(f"Graph built: {G.number_of_nodes()} nodes, {G.number_of_edges()} edges")

communities = cluster(G)
print(f"Clustered: {len(communities)} communities")

cohesion = score_all(G, communities)
gods = god_nodes(G)
surprises = surprising_connections(G, communities)
labels = {cid: 'Community ' + str(cid) for cid in communities}
questions = suggest_questions(G, communities, labels)

report = generate(G, communities, cohesion, labels, gods, surprises, detection, {'input': 0, 'output': 0}, 'Assets/Scripts', suggested_questions=questions)
Path('E:/CodeForJob/Cool/graphify-out/GRAPH_REPORT.md').write_text(report)
to_json(G, communities, 'E:/CodeForJob/Cool/graphify-out/graph.json')

analysis = {
    'communities': {str(k): v for k, v in communities.items()},
    'cohesion': {str(k): v for k, v in cohesion.items()},
    'gods': gods,
    'surprises': surprises,
    'questions': questions,
}
Path('E:/CodeForJob/Cool/.graphify_analysis.json').write_text(json.dumps(analysis, indent=2))
Path('E:/CodeForJob/Cool/.graphify_labels.json').write_text(json.dumps({str(k): v for k, v in labels.items()}))

print(f"Graph: {G.number_of_nodes()} nodes, {G.number_of_edges()} edges, {len(communities)} communities")
print(f"God nodes: {gods}")
print(f"Surprising connections: {surprises[:5]}")
print("Report and graph saved.")