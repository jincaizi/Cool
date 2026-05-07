import json
from pathlib import Path
import networkx as nx
from networkx.readwrite import json_graph

with open('E:/CodeForJob/Cool/.graphify_analysis.json', 'r', encoding='utf-8') as f:
    analysis = json.load(f)
with open('E:/CodeForJob/Cool/graphify-out/graph.json', 'r', encoding='utf-8') as f:
    graph_data = json.load(f)

G = json_graph.node_link_graph(graph_data, edges='links')
communities = {int(k): v for k, v in analysis['communities'].items()}

# Top 20 communities by size
top_communities = sorted(communities.items(), key=lambda x: -len(x[1]))[:25]

for cid, members in top_communities:
    # Get labels of top nodes in this community by degree
    community_nodes = [(n, G.degree(n)) for n in members]
    community_nodes.sort(key=lambda x: -x[1])
    top_labels = [G.nodes[n].get('label', n) for n, _ in community_nodes[:8]]
    print(f"Community {cid} ({len(members)} nodes): {', '.join(top_labels)}")
