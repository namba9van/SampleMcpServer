#!/usr/bin/env python3
"""Static repository validation using only Python stdlib."""
from __future__ import annotations
import re, sqlite3, sys, xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[1]
REQUIRED = [
    "SampleMcpServer.csproj", "SampleMcpServer.sln", "Program.cs", "README.md", "LICENSE",
    ".gitignore", ".env.example", "CHANGELOG.md",
    "docs/ARCHITECTURE.md", "docs/CONFIGURATION.md", "docs/SECURITY.md", "docs/MEMORY_POLICY.md", "docs/TOOLS.md",
    "HANDOFF.md", "SECURITY.md",
    "Services/ModelMemoryService.cs", "Services/TriggerAutomationService.cs",
    "Services/RuntimeGovernanceService.cs", "Services/AgentHostService.cs",
    "Tools/MemoryTools.cs", "Tools/TriggerActionTools.cs", "Tools/RuntimeTools.cs", "Tools/RAGTool.cs",
]

def fail(msg: str) -> None:
    print(f"FAIL: {msg}")
    raise SystemExit(1)

def lex_code(text: str) -> str:
    out=[]; i=0; n=len(text); state="code"; rawq=0
    while i<n:
        ch=text[i]
        if state=="code":
            if text.startswith("//",i): state="line"; i+=2; continue
            if text.startswith("/*",i): state="block"; i+=2; continue
            raw=re.match(r'\$*("{3,})',text[i:])
            if raw: rawq=len(raw.group(1)); state="raw"; i+=len(raw.group(0)); continue
            if text.startswith('$@"',i) or text.startswith('@$"',i): state="verb"; i+=3; continue
            if text.startswith('@"',i): state="verb"; i+=2; continue
            if text.startswith('$"',i): state="string"; i+=2; continue
            if ch=='"': state="string"; i+=1; continue
            if ch=="'": state="char"; i+=1; continue
            out.append(ch); i+=1; continue
        if state=="line":
            if ch=='\n': out.append(ch); state="code"
            i+=1; continue
        if state=="block":
            if text.startswith("*/",i): state="code"; i+=2
            else: i+=1
            continue
        if state=="raw":
            q='"'*rawq
            if text.startswith(q,i): state="code"; i+=rawq
            else: i+=1
            continue
        if state=="verb":
            if text.startswith('""',i): i+=2
            elif ch=='"': state="code"; i+=1
            else: i+=1
            continue
        if state in {"string","char"}:
            q='"' if state=="string" else "'"
            if ch=='\\': i+=2
            elif ch==q: state="code"; i+=1
            else: i+=1
    if state not in {"code","line"}: fail(f"unterminated C# lexical state: {state}")
    return ''.join(out)

def check_delimiters(path: Path) -> None:
    code=lex_code(path.read_text(encoding="utf-8")); stack=[]; pairs={')':'(',']':'[','}':'{'}
    for pos,ch in enumerate(code):
        if ch in '([{': stack.append((ch,pos))
        elif ch in ')]}':
            if not stack or stack[-1][0] != pairs[ch]: fail(f"delimiter mismatch in {path.relative_to(ROOT)} near {pos}")
            stack.pop()
    if stack: fail(f"unclosed delimiter in {path.relative_to(ROOT)}")

def main() -> None:
    missing=[p for p in REQUIRED if not (ROOT/p).is_file()]
    if missing: fail("missing required files: "+", ".join(missing))

    garbage={"bin","obj",".vs",".idea","TestResults","__pycache__"}
    found=sorted({p.name for p in ROOT.rglob('*') if p.is_dir() and p.name in garbage})
    if found: fail("generated/IDE directories present: "+", ".join(found))

    cs_files=sorted(ROOT.rglob('*.cs'))
    marker=re.compile(r'\b(TODO|FIXME|HACK|XXX)\b',re.I)
    for p in cs_files:
        text=p.read_text(encoding='utf-8')
        if marker.search(text): fail(f"development marker in {p.relative_to(ROOT)}")
        check_delimiters(p)

    names={}
    for p in sorted((ROOT/'Tools').glob('*.cs')):
        for name in re.findall(r'McpServerTool\(Name\s*=\s*"([^"]+)"\)',p.read_text(encoding='utf-8')):
            if not re.fullmatch(r'[a-z][a-z0-9_]*',name): fail(f"invalid tool name {name!r} in {p.name}")
            if name in names: fail(f"duplicate tool name {name!r}: {p.name} and {names[name].name}")
            names[name]=p
    if len(names)<50: fail(f"unexpectedly low tool count: {len(names)}")

    for p in sorted((ROOT/'Services').glob('*.cs')):
        blocks=[b for b in re.findall(r'"""(.*?)"""',p.read_text(encoding='utf-8'),flags=re.S) if 'CREATE TABLE' in b]
        if not blocks: continue
        db=sqlite3.connect(':memory:')
        try:
            for sql in blocks: db.executescript(sql)
        except sqlite3.Error as exc: fail(f"SQLite schema failed in {p.name}: {exc}")
        finally: db.close()

    # Security hardening that must not regress.
    for name in ["AgentHostService.cs","TriggerAutomationService.cs","RuntimeGovernanceService.cs"]:
        if "AllowAutoRedirect = false" not in (ROOT/'Services'/name).read_text(encoding='utf-8'):
            fail(f"redirect hardening missing in {name}")

    # Environment variables referenced in code should be documented, except explicit legacy aliases.
    code='\n'.join(p.read_text(encoding='utf-8') for p in cs_files)
    env=set(re.findall(r'GetEnvironmentVariable\("([A-Za-z0-9_]+)"\)',code))
    env.update(re.findall(r'Read(?:Bool|Int|Double)\("([A-Za-z0-9_]+)"',code))
    env.update(re.findall(r'Read(?:IntEnv|DoubleEnvironment)\("([A-Za-z0-9_]+)"',code))
    documented=(ROOT/'.env.example').read_text(encoding='utf-8')
    aliases={"GUTHUB_TOKEN","WEB_SEARCH_FirecrawApiKey","WEB_SEARCH_duckduckgoRegion"}
    absent=sorted(v for v in env-aliases if v not in documented)
    if absent: fail("environment variables missing from .env.example: "+", ".join(absent))

    # Basic secret scan. Example placeholders and empty values are fine.
    secret_patterns=[r'github_pat_[A-Za-z0-9_]{20,}', r'sk-[A-Za-z0-9]{20,}', r'Bearer\s+[A-Za-z0-9._-]{30,}']
    for p in ROOT.rglob('*'):
        if not p.is_file() or p.suffix in {'.zip','.png','.jpg','.jpeg','.pdf'}: continue
        text=p.read_text(encoding='utf-8',errors='ignore')
        for pat in secret_patterns:
            if re.search(pat,text): fail(f"possible secret in {p.relative_to(ROOT)}")

    try:
        ET.parse(ROOT/"SampleMcpServer.csproj")
    except ET.ParseError as exc:
        fail(f"invalid csproj XML: {exc}")

    csproj=(ROOT/'SampleMcpServer.csproj').read_text(encoding='utf-8')
    for package in ["ModelContextProtocol","Microsoft.Data.Sqlite","PdfPig"]:
        if f'Include="{package}"' not in csproj: fail(f"required package missing: {package}")
    for old in ["Microsoft.KernelMemory","SemanticKernel.Connectors.InMemory"]:
        if old in csproj: fail(f"removed legacy dependency still present: {old}")

    print(f"PASS: {len(cs_files)} C# files, {len(names)} unique MCP tools, SQLite schemas valid, env/docs/security checks valid")

if __name__ == '__main__': main()
