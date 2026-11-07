"""Read-only control-flow checks of the supplied upstream Defender methods.

Does not import/start the app, run PowerShell, change Defender or open Windows
Security. Live disable/re-enable acceptance remains a separate VM requirement.
"""
import ast
import hashlib
import json
from pathlib import Path
import sys
from types import SimpleNamespace

source = Path(sys.argv[1]).read_bytes()
assert hashlib.sha256(source).hexdigest() == '2cd01d8def2d450af358d54c4a025b7cda0de0f0ddf18584800017c03551fd74', 'Review changed upstream methods before running this isolated fixture'
tree = ast.parse(source.decode('utf-8-sig'))
names = {'_defender_payload', 'defender_status', 'defender_set'}
constants = {'_DEF_READ', '_DEF_DISABLE', '_DEF_ENABLE'}
classes = [n for n in tree.body if isinstance(n, ast.ClassDef) and any(isinstance(m, ast.FunctionDef) and m.name == 'defender_set' for m in n.body)]
assert len(classes) == 1, 'Expected one upstream Defender controller'
members = []
for member in classes[0].body:
    if isinstance(member, ast.Assign) and len(member.targets) == 1 and isinstance(member.targets[0], ast.Name) and member.targets[0].id in constants:
        assert isinstance(member.value, ast.Constant) and isinstance(member.value.value, str)
        members.append(member)
    elif isinstance(member, ast.FunctionDef) and member.name in names:
        member.decorator_list = []
        members.append(member)
assert len(members) == 6, 'Upstream Defender interface changed; review it before executing this fixture'
# Only the reviewed methods execute, with shell/OS operations replaced below.
controller = ast.ClassDef(name='Controller', bases=[], keywords=[], body=members, decorator_list=[])
module = ast.fix_missing_locations(ast.Module(body=[controller], type_ignores=[]))
calls, opened = [], []
state = {'present': True, 'realtime': True, 'tamper': True, 'amservice': True,
         'behavior': True, 'onaccess': True, 'ioav': True, 'nis': True}
environment = {'json': json, 'os': SimpleNamespace(startfile=opened.append)}

def ps(command):
    calls.append(command)
    c = environment['Controller']
    if command == c._DEF_READ:
        return 0, json.dumps(state)
    if command == c._DEF_DISABLE:
        state.update(realtime=False, behavior=False, onaccess=False, ioav=False, nis=False)
        return 0, 'done'
    if command == c._DEF_ENABLE:
        state.update(realtime=True, behavior=True, onaccess=True, ioav=True, nis=True)
        return 0, 'done'
    raise AssertionError('Unexpected shell action in upstream method')

environment['ps'] = ps
exec(compile(module, '<reviewed-upstream-defender-methods>', 'exec'), environment)
app = environment['Controller']()
blocked = app.defender_set(False)
assert not blocked['ok'] and blocked['tamper'] and opened == ['windowsdefender://threatsettings']
assert calls == [app._DEF_READ], 'Protected case must not attempt preference changes'
state['tamper'] = False
calls.clear()
disabled = app.defender_set(False)
assert disabled['ok'] and not disabled['status']['active'] and calls == [app._DEF_READ, app._DEF_DISABLE, app._DEF_READ]
# The always-resident engine is not mistaken for real-time protection being on.
assert disabled['status']['engine_resident'] is True
calls.clear()
enabled = app.defender_set(True)
assert enabled['ok'] and enabled['status']['active'] and calls == [app._DEF_READ, app._DEF_ENABLE, app._DEF_READ]
state['present'] = False
calls.clear()
absent = app.defender_set(False)
assert absent['ok'] and not absent['status']['present'] and calls == [app._DEF_READ]
report = {'sourceSha256': hashlib.sha256(source).hexdigest(), 'checks': 4,
          'tamperGuard': True, 'normalDisableRouting': True, 'normalEnableRouting': True,
          'missingComponentHandling': True, 'windowsSettingsChanged': False,
          'liveOffOnTest': False, 'quarantinedRemoverRequiredForNormalToggle': False}
print(json.dumps(report, indent=2))
