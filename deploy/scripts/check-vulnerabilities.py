import json
import sys

report = json.load(open(sys.argv[1], encoding='utf-8-sig'))
if any(item.get('level', '').lower() == 'error' for item in report.get('log', [])):
    raise SystemExit('Dependency scanner returned errors')
findings = []
for project in report.get('projects', []):
    for framework in project.get('frameworks', []):
        for package in framework.get('topLevelPackages', []) + framework.get('transitivePackages', []):
            for vulnerability in package.get('vulnerabilities', []):
                if vulnerability.get('severity', '').lower() in ('high', 'critical'):
                    findings.append(package['id'])
if findings:
    raise SystemExit('High/critical vulnerable packages: ' + ', '.join(sorted(set(findings))))
