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

# Label the top 25 communities
# Community ID -> (label, confidence)
community_labels = {
    0: "Animation SMB Callbacks",
    1: "KCP Networking Examples",
    2: "FSM Attack Layer",
    3: "Character + Buff System",
    4: "FSM Core / 3C Root",
    5: "Monster AI + NPC Animation",
    6: "UI + Debug + Weapon",
    7: "Monster Combat System",
    8: "StateCoordinator + KCP Session",
    9: "Spawning + Camera + Pool",
    10: "Combat Attack Shapes",
    11: "Debug Window + Confirm",
    12: "KCP Core Networking",
    13: "Character Update Loop",
    14: "Bag UI Panel",
    15: "Skill Input Processing",
    16: "Logging Infrastructure",
    17: "Animation Driver API",
    18: "Message Codec Layer",
    19: "KCP Socket / Buffer",
    20: "Hit FSM",
    21: "Input Adapters",
    22: "UIPanel Lifecycle",
    23: "AI Behaviour States",
    24: "Skill State Machine",
}

# Save labels
with open('E:/CodeForJob/Cool/.graphify_labels.json', 'w', encoding='utf-8') as f:
    json.dump({str(k): v for k, v in community_labels.items()}, f)

# Regenerate report with labels
from graphify.build import build_from_json
from graphify.cluster import score_all
from graphify.analyze import god_nodes, surprising_connections, suggest_questions
from graphify.report import generate

with open('E:/CodeForJob/Cool/.graphify_extract.json', 'r', encoding='utf-8') as f:
    extraction = json.load(f)
with open('E:/CodeForJob/Cool/.graphify_detect.json', 'r', encoding='utf-8') as f:
    detection = json.load(f)

G = build_from_json(extraction)
cohesion = score_all(G, communities)
gods = god_nodes(G)
surprises = surprising_connections(G, communities)
questions = suggest_questions(G, communities, community_labels)

report = generate(G, communities, cohesion, community_labels, gods, surprises, detection, {'input': 0, 'output': 0}, 'Assets/Scripts', suggested_questions=questions)
with open('E:/CodeForJob/Cool/graphify-out/GRAPH_REPORT.md', 'w', encoding='utf-8') as f:
    f.write(report)

# Update analysis with labels
analysis['labels'] = community_labels
analysis['questions'] = questions
with open('E:/CodeForJob/Cool/.graphify_analysis.json', 'w', encoding='utf-8') as f:
    json.dump(analysis, f, indent=2, ensure_ascii=False)

# Update HTML with labels
from graphify.export import to_html
to_html(G, communities, 'E:/CodeForJob/Cool/graphify-out/graph.html', community_labels=community_labels)

print("Updated: GRAPH_REPORT.md, graph.html, .graphify_analysis.json, .graphify_labels.json")
print("Communities labeled:", len(community_labels))