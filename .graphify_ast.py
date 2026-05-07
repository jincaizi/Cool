if __name__ == '__main__':
    import json
    from graphify.extract import collect_files, extract
    from pathlib import Path

    detect = json.load(open('E:/CodeForJob/Cool/.graphify_detect.json'))
    code_files = []
    for f in detect['files'].get('code', []):
        code_files.extend(collect_files(Path(f)) if Path(f).is_dir() else [Path(f)])

    print(f"Extracting {len(code_files)} code files...")
    result = extract(code_files)
    open('E:/CodeForJob/Cool/.graphify_ast.json', 'w').write(json.dumps(result, indent=2))
    print(f"AST: {len(result['nodes'])} nodes, {len(result['edges'])} edges")
