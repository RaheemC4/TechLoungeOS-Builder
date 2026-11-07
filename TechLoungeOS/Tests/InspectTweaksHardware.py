"""Read-only source regression probe. No driver, registry or vendor tool runs.

Usage: python InspectTweaksHardware.py <TechLoungeTweaks/src>
The named source is parsed, not imported. Execute only the inspected functions
against controlled values and pure write collectors, never the real backend.
"""
import ast
import hashlib
import json
import re
import sys
from pathlib import Path


source = Path(sys.argv[1]).resolve()
engine_path = source / "tweaks_engine.py"
engine = ast.parse(engine_path.read_text(encoding="utf-8"))
functions = {n.name: n for n in engine.body if isinstance(n, ast.FunctionDef)}
gpu = {"_gpu_cache": None}
exec(compile(ast.Module(body=[functions["gpu_vendors"]], type_ignores=[]), str(engine_path), "exec"), gpu)
build = functions["build_tweaks"]
gpu_blocks = [n for n in build.body if isinstance(n, ast.If) and isinstance(n.test, ast.Call)
              and isinstance(n.test.func, ast.Attribute) and isinstance(n.test.func.value, ast.Name)
              and n.test.func.value.id == "vendors"]
assert len(gpu_blocks) == 2, "Unexpected GPU branch layout needs review"
cases = []
for label, code, names, expected in [
    ("Intel only", 0, "Intel(R) Arc(TM) A770 Graphics|Intel(R) UHD Graphics 770", (False, False)),
    ("AMD only", 0, "AMD Radeon RX 7900 XTX", (False, True)),
    ("AMD and Intel hybrid", 0, "Intel(R) UHD Graphics|AMD Radeon Graphics", (False, True)),
    ("NVIDIA and Intel hybrid", 0, "Intel(R) Iris(R) Xe Graphics|NVIDIA GeForce RTX 4070 Laptop GPU", (True, False)),
    ("NVIDIA and AMD hybrid", 0, "AMD Radeon Graphics|NVIDIA GeForce RTX 4090", (True, True)),
    ("Basic Display Adapter", 0, "Microsoft Basic Display Adapter", (False, False)),
    ("failed detection", 1, "SUBPROCESS_ERROR: WMI unavailable", (False, False)),
]:
    gpu.update(_gpu_cache=None, ps=lambda command, rc=code, text=names: (rc, text))
    vendors = gpu["gpu_vendors"]()
    assert (vendors["nvidia"], vendors["amd"]) == expected, label
    keys = []
    noop_triple = (lambda: None,) * 3
    context = {"vendors": vendors, "add": lambda key, *args, **kwargs: keys.append(key),
               "HKLM": "HKLM", "NV_KEY": "NVIDIA-specific registry path", "AMD_CLASS": "AMD-specific registry path",
               "simple_reg": lambda *args, **kwargs: noop_triple,
               "t_nv_telemetry": lambda: noop_triple, "amd_class_key": lambda: "AMD-specific registry path"}
    exec(compile(ast.Module(body=gpu_blocks, type_ignores=[]), str(engine_path), "exec"), context)
    assert len([k for k in keys if k.startswith("nv_")]) == (4 if expected[0] else 0), label
    assert len([k for k in keys if k.startswith("amd_")]) == (4 if expected[1] else 0), label
    cases.append({"case": label, "vendors": vendors, "offered_gpu_tweaks": keys})

# Compare every actual registry write in the approved source subset with the
# Builder's frozen positive list, so upstream changes cannot silently widen it.
catalog = json.loads((source / "debloat_catalog.json").read_text(encoding="utf-8"))
approved = set(catalog["existing_recommended"]) - {"pause_windows_updates"}
assert len(approved) == 10 and not any(k.startswith(("nv_", "amd_", "defender")) for k in approved)
writes = []
def simple_reg(hive, path, name, on_value, off_value, **kwargs):
    return (lambda: writes.append((hive, path, name, on_value)), None, None)
def add(key, name, desc, category, triple, **kwargs):
    triple[0]()
context = {"add": add, "simple_reg": simple_reg, "HKCU": "User", "HKLM": "Machine",
           "reg_set": lambda hive, path, name, value: writes.append((hive, path, name, value))}
selected = [n for n in build.body if isinstance(n, ast.Expr) and isinstance(n.value, ast.Call)
            and isinstance(n.value.func, ast.Name) and n.value.func.id == "add"
            and n.value.args and isinstance(n.value.args[0], ast.Constant) and n.value.args[0].value in approved]
assert len(selected) == 10
exec(compile(ast.Module(body=selected, type_ignores=[]), str(engine_path), "exec"), context)
builder_path = Path(__file__).resolve().parents[1] / "Builder" / "RecommendedProfile.cs"
pattern = r'new\("[^"]+", "(User|Machine)", @"([^"]+)", "([^"]+)", (\d+)\)'
actual = [(scope, path, name, int(value)) for scope, path, name, value in re.findall(pattern, builder_path.read_text(encoding="utf-8"))]
assert len(actual) == 14 and sorted(actual) == sorted(writes), "Reviewed profile differs from original source"

# The standalone Inspector apply function has no hardware gate. Prove that
# behaviour with an inert vendor runner; do not mistake it for no-op safety.
profile_path = source / "nvprofile.py"
profile_ast = ast.parse(profile_path.read_text(encoding="utf-8"))
apply = next(n for n in profile_ast.body if isinstance(n, ast.FunctionDef) and n.name == "apply_profile")
attempts = []
context = {"status": lambda: {"tool": True}, "find_tool": lambda: "inert-inspector",
           "find_profile": lambda: "inert-profile", "is_applied": lambda: False,
           "_run_nip": lambda tool, profile: (attempts.append((tool, profile)) or False, "inert failure")}
exec(compile(ast.Module(body=[apply], type_ignores=[]), str(profile_path), "exec"), context)
assert context["apply_profile"](True)["ok"] is False and attempts == [("inert-inspector", "inert-profile")]
result = {"evidence": "controlled source execution; no physical GPU or driver test",
          "hardware_cases": cases, "reviewed_profile_values_match": len(actual),
          "manual_inspector_apply": "invokes vendor tool without a hardware check; harmless no-op not established",
          "source_sha256": {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in [engine_path, profile_path, source / "debloat_catalog.json"]}}
print(json.dumps(result, indent=2))
