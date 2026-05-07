import json
from graphify.build import build_from_json
from graphify.export import to_html
from pathlib import Path

with open('E:/CodeForJob/Cool/.graphify_analysis.json', 'r', encoding='utf-8') as f:
    analysis = json.load(f)
with open('E:/CodeForJob/Cool/.graphify_labels.json', 'r', encoding='utf-8') as f:
    labels_raw = json.load(f)
with open('E:/CodeForJob/Cool/.graphify_extract.json', 'r', encoding='utf-8') as f:
    extraction = json.load(f)

G = build_from_json(extraction)
communities = {int(k): v for k, v in analysis['communities'].items()}
labels = {int(k): v for k, v in labels_raw.items()} if labels_raw else {int(k): 'Community '+str(k) for k in communities.keys()}
to_html(G, communities, 'E:/CodeForJob/Cool/graphify-out/graph.html', community_labels=labels)
print('HTML saved -', G.number_of_nodes(), 'nodes,', len(communities), 'communities')
