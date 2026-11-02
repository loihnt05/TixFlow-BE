"""Export OpenAPI from the locally built API without connecting to application data."""
import hashlib
import json
import os
from pathlib import Path
import socket
import subprocess
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
DLL = ROOT / "src/TixFlow.Api/bin/Release/net8.0/TixFlow.Api.dll"
OUT = ROOT / "docs/release"
TEMP = ROOT / "artifacts/phase13/openapi"
TEMP.mkdir(parents=True, exist_ok=True)
OUT.mkdir(parents=True, exist_ok=True)
with socket.socket() as listener:
    listener.bind(("127.0.0.1", 0))
    port = listener.getsockname()[1]
env = dict(os.environ)
env.update({"ASPNETCORE_ENVIRONMENT": "Development", "ASPNETCORE_URLS": f"http://127.0.0.1:{port}",
            "ConnectionStrings__Postgres": "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=fixture-only;Timeout=1",
            "ConnectionStrings__Redis": "127.0.0.1:1,abortConnect=false", "Assistant__Provider": "Disabled",
            "Assistant__ApiKey": "", "Assistant__KeyDirectory": str(TEMP / "assistant-keys"),
            "Tickets__KeyDirectory": str(TEMP / "ticket-keys"), "RabbitMq__Host": "127.0.0.1",
            "RabbitMq__Username": "unused", "RabbitMq__Password": "fixture-only"})
with (TEMP / "process.log").open("wb") as log:
    process = subprocess.Popen(["dotnet", str(DLL)], cwd=DLL.parent, env=env, stdout=log, stderr=log)
    try:
        for attempt in range(45):
            if process.poll() is not None:
                raise RuntimeError("API exited; inspect artifacts/phase13/openapi/process.log")
            try:
                with urllib.request.urlopen(f"http://127.0.0.1:{port}/swagger/v1/swagger.json", timeout=2) as response:
                    spec = json.load(response)
                break
            except OSError:
                time.sleep(1)
        else:
            raise TimeoutError("OpenAPI export timed out")
        assert "paths" in spec and "/api/v1/assistant/confirm-hold" in spec["paths"]
        (OUT / "openapi.json").write_text(json.dumps(spec, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        (OUT / "openapi-provenance.json").write_text(json.dumps({
            "assembly": DLL.relative_to(ROOT).as_posix(), "assembly_sha256": hashlib.sha256(DLL.read_bytes()).hexdigest(),
            "paths": len(spec["paths"]), "environment": "Development, unavailable fixture database, no business requests",
            "source": "Release assembly built by phase13 regression; see TRX and review package source hashes"}, indent=2) + "\n", encoding="utf-8")
        print(f"Exported {len(spec['paths'])} OpenAPI paths")
    finally:
        process.terminate()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=10)
