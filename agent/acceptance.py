"""Bounded live multiplayer goal replacement/stop acceptance; no UI timing required."""
import json
import time
import uuid

import run as agent


def main():
    state = agent.observation(agent.mode_request('multiplayer_server'))
    agent.validate_snapshot(state, 'multiplayer_server')
    if state.get('goalId'):
        raise RuntimeError('Finish or stop the current goal before starting acceptance.')
    context = agent.multiplayer_identity(state)
    result = agent.mode_request('multiplayer_server', 'test-goals', state['stopRevision'], context)
    identity = uuid.UUID(result['testId']).hex
    report = agent.MAILBOX / f'acceptance-{identity}.json'
    print('Acceptance started: ' + identity, flush=True)
    deadline = time.monotonic() + 70
    while time.monotonic() < deadline:
        if report.exists():
            payload = json.loads(report.read_text(encoding='utf-8-sig'))
            assert payload['id'] == identity
            output = agent.ROOT / '.local/test-results' / report.name
            output.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding='utf-8')
            print('Report: ' + str(output), flush=True)
            print(payload['outcome'] + ': ' + payload['reason'], flush=True)
            return 0 if payload['outcome'] == 'passed' else 1
        time.sleep(0.2)
    raise RuntimeError('Server acceptance timed out. Query /tpamp status before retrying; do not replay blindly.')


if __name__ == '__main__':
    raise SystemExit(main())
