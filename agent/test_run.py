"""Offline checks for sequential actions, cancellation and factual feedback."""
import copy
import json
import os
from pathlib import Path
import shutil
import tempfile
import threading
import time
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import uuid

import run as agent


class FakeGame:
    def __init__(self):
        self.state = dict(gameMenu=False, netMode=0, paused=False, tick=100, stopRevision=0,
                          player={}, actor=dict(slot=1, x=100, y=100, life=100, dead=False,
                          deathCount=0, respawnCount=0, moveTicks=0, jumpTicks=0, attackTicks=0,
                          itemAnimation=0, item=24, combatEvents=[]))
        self.commands = []
        self.blocked = self.reject = self.cancel = False
        self.hold_active = False

    def request(self, command, expected_revision=None):
        if expected_revision is not None and expected_revision != self.state['stopRevision']:
            raise agent.TaskInterrupted('stale task')
        actor = self.state['actor']
        if command == 'status':
            self.state['tick'] += 6
            if self.cancel:
                self.state['stopRevision'] += 1
            for key in ('moveTicks', 'jumpTicks', 'attackTicks', 'itemAnimation'):
                if self.hold_active:
                    continue
                if key == 'moveTicks' and actor[key] and not self.blocked:
                    actor['x'] += 6
                actor[key] = max(0, actor[key] - 6)
        else:
            self.commands.append((command, copy.deepcopy(self.state)))
            if command.startswith('/tpa right') and not self.reject:
                actor['moveTicks'] = 30
            if command.startswith('/tpa attack') and not self.reject:
                actor['attackTicks'] = 18
            if command == '/tpa stop':
                self.state['stopRevision'] += 1
                self.hold_active = False
                actor['moveTicks'] = actor['attackTicks'] = 0
        return dict(ok=True, accepted=not self.reject, reply='test', state=copy.deepcopy(self.state))


class ActionTests(unittest.TestCase):
    def setUp(self):
        self.game = FakeGame()
        self.control = agent.TaskControl(copy.deepcopy(self.game.state))
        self.requests = patch.object(agent, 'game_request', self.game.request)
        self.requests.start()
        self.addCleanup(self.requests.stop)

    def execute(self, commands):
        with patch.object(agent.time, 'sleep'):
            return agent.execute_actions(commands, 0, self.control)

    def test_move_finishes_before_attack_and_no_hit_is_not_success(self):
        result = self.execute(['/tpa right 0.5', '/tpa attack right 0.3'])
        self.assertEqual(self.game.commands[1][1]['actor']['moveTicks'], 0)
        self.assertGreaterEqual(result['actions'][0]['end_tick'] - result['actions'][0]['start_tick'], 30)
        self.assertEqual([a['status'] for a in result['actions']], ['completed', 'unconfirmed'])
        self.assertFalse(result['ok'])

    def test_blocked_move_does_not_issue_following_attack(self):
        self.game.blocked = True
        result = self.execute(['/tpa right 0.5', '/tpa attack right'])
        self.assertEqual(result['actions'][0]['status'], 'blocked')
        self.assertEqual(len(self.game.commands), 1)

    def test_rejected_command_does_not_issue_following_move(self):
        self.game.reject = True
        result = self.execute(['/tpa attack right', '/tpa right'])
        self.assertEqual(result['actions'][0]['status'], 'rejected')
        self.assertEqual(len(self.game.commands), 1)

    def test_directional_jump_and_drop_validate_duration(self):
        for command in ('/tpa jump', '/tpa jump left 1.5', '/tpa jump right', '/tpa down 0.25'):
            self.assertEqual(agent.validate_actions({'commands': [command]})[0], [command])
        for command in ('/tpa jump left 0', '/tpa jump right 11', '/tpa down 0', '/tpa jump up'):
            with self.assertRaises(ValueError):
                agent.validate_actions({'commands': [command]})

    def test_mining_and_inventory_command_limits(self):
        for command in ('/tpa equip pick', '/tpa select 49', '/tpa mine 2099 294 2'):
            self.assertEqual(agent.validate_actions({'commands': [command]})[0], [command])
        for command in ('/tpa select 50', '/tpa mine 10 10 0', '/tpa mine 10 10 11', '/tpa mine -1 10'):
            with self.assertRaises(ValueError):
                agent.validate_actions({'commands': [command]})

    def test_navigation_commands_and_model_cannot_change_range(self):
        for command in ('/tpa goto 33582.25', '/tpa goto 33582 5', '/tpa home', '/tpa home 2'):
            self.assertEqual(agent.validate_actions({'commands': [command]})[0], [command])
        for command in ('/tpa goto -1', '/tpa goto 300 0', '/tpa home 11', '/tpa range off'):
            with self.assertRaises(ValueError):
                agent.validate_actions({'commands': [command]})

    def test_navigation_requires_new_confirmed_arrival(self):
        original = self.game.request
        for status, sequence, x in (('arrived', 2, 200), ('arrived', 1, 200), ('arrived', 2, 220), ('blocked', 2, 180)):
            with self.subTest(status=status, sequence=sequence, x=x):
                self.game.commands.clear()
                self.game.state['actor'].update(x=100, velocityX=0,
                    navigation=dict(active=False, sequence=1, status='arrived', targetX=100))

                def request(command, expected_revision=None):
                    if command.startswith('/tpa goto'):
                        self.game.state['actor'].update(x=x, navigation=dict(active=False,
                            sequence=sequence, status=status, targetX=200, reason='test_block' if status=='blocked' else ''))
                    return original(command, expected_revision)

                with patch.object(agent, 'game_request', request):
                    result = self.execute(['/tpa goto 200', '/tpa attack right'])
                passed = status == 'arrived' and sequence == 2 and x == 200
                self.assertEqual(result['actions'][0]['status'], 'completed' if passed else 'blocked' if status=='blocked' else 'unconfirmed')
                self.assertEqual(len(self.game.commands), 2 if passed else 1)

    def test_mining_requires_matching_native_break_before_following_action(self):
        original = self.game.request
        for broken, event_x in ((True, 10), (False, 10), (True, 11)):
            with self.subTest(broken=broken, event_x=event_x):
                self.game.commands.clear()
                self.game.state['actor']['mining'] = None

                def request(command, expected_revision=None):
                    if command.startswith('/tpa mine'):
                        self.game.state['actor']['mining'] = dict(sequence=1, status='broken',
                            x=10, y=20, item=3509, events=[dict(broken=broken, actor=1,
                            item=3509, x=event_x, y=20)])
                    return original(command, expected_revision)

                with patch.object(agent, 'game_request', request):
                    result = self.execute(['/tpa mine 10 20', '/tpa right'])
                confirmed = broken and event_x == 10
                self.assertEqual(result['actions'][0]['status'], 'completed' if confirmed else 'unconfirmed')
                self.assertEqual(len(self.game.commands), 2 if confirmed else 1)

    def test_partial_movement_followed_by_block_is_not_success(self):
        original = self.game.request

        def request(command, expected_revision=None):
            if command == 'status' and self.game.state['actor']['moveTicks'] > 0:
                self.game.state['actor'].update(x=110, moveTicks=0,
                                               blockedReason='no_progress_after_jump_attempts')
            return original(command, expected_revision)

        with patch.object(agent, 'game_request', request):
            result = self.execute(['/tpa right 5', '/tpa attack right'])
        self.assertEqual(result['actions'][0]['status'], 'blocked')
        self.assertGreater(result['actions'][0]['deltaX'], 2)
        self.assertEqual(len(self.game.commands), 1)

    def test_stop_during_movement_preserves_partial_result(self):
        original = self.game.request

        def request(command, expected_revision=None):
            reply = original(command, expected_revision)
            if command.startswith('/tpa right'):
                self.game.cancel = True
            return reply

        with patch.object(agent, 'game_request', request):
            result = self.execute(['/tpa right 0.5', '/tpa attack right'])
        self.assertEqual(result['outcome'], 'interrupted')
        self.assertEqual(result['actions'][0]['status'], 'interrupted')
        self.assertEqual(len(self.game.commands), 1)

    def test_stop_between_observation_and_dispatch_rejects_stale_action(self):
        original = self.game.request

        def request(command, expected_revision=None):
            if command.startswith('/tpa right'):
                self.game.state['stopRevision'] += 1
            return original(command, expected_revision)

        with patch.object(agent, 'game_request', request):
            result = self.execute(['/tpa right', '/tpa attack right'])
        self.assertEqual(result['outcome'], 'interrupted')
        self.assertEqual(self.game.commands, [])

    def test_paused_action_is_unknown_and_does_not_continue(self):
        self.game.state['paused'] = True
        with patch.object(agent, 'RESUME_TIMEOUT', 0.2):
            result = self.execute(['/tpa right', '/tpa attack right'])
        self.assertEqual(result['outcome'], 'unknown')
        self.assertEqual(self.game.commands, [])

    def test_stop_during_api_wait_does_not_wait_for_response(self):
        release = threading.Event()
        self.game.cancel = True
        try:
            with patch.object(agent, 'chat', side_effect=lambda *a: release.wait(3)):
                started = time.monotonic()
                with self.assertRaises(agent.TaskInterrupted):
                    agent.chat_interruptibly([], self.control)
                self.assertLess(time.monotonic() - started, 1)
        finally:
            release.set()
        self.assertEqual(self.game.commands, [])

    def test_old_hit_and_other_attacker_are_not_confirmation(self):
        self.game.state['actor']['combatEvents'] = [
            dict(kind='item-hit', tick=90, attacker=1, item=24, damage=10),
            dict(kind='item-hit', tick=999, attacker=0, item=24, damage=10)]
        result = self.execute(['/tpa attack right'])
        self.assertEqual(result['actions'][0]['status'], 'unconfirmed')

    def test_hit_before_last_twelve_events_is_preserved(self):
        original = self.game.request

        def request(command, expected_revision=None):
            if command.startswith('/tpa attack'):
                self.game.state['actor']['combatEvents'] = [
                    dict(kind='item-hit', tick=self.game.state['tick'] + 1, attacker=1, item=24, damage=10)
                ] + [dict(kind='hurt', tick=self.game.state['tick'] + 2) for _ in range(12)]
            return original(command, expected_revision)

        with patch.object(agent, 'game_request', request):
            result = self.execute(['/tpa attack right'])
        self.assertEqual(result['actions'][0]['status'], 'completed')
        self.assertEqual(len(result['actions'][0]['item_hits']), 1)

    def test_death_interrupts_batch_instead_of_counting_respawn_as_movement(self):
        original = self.game.request

        def request(command, expected_revision=None):
            reply = original(command, expected_revision)
            if command.startswith('/tpa right'):
                self.game.state['actor'].update(dead=True, deathCount=1, x=10000)
            return reply

        with patch.object(agent, 'game_request', request):
            result = self.execute(['/tpa right', '/tpa attack right'])
        self.assertEqual(result['outcome'], 'interrupted')
        self.assertEqual(len(self.game.commands), 1)

    def test_run_discards_late_api_reply_and_does_not_stop_newer_task(self):
        release = threading.Event()
        self.game.cancel = True
        report = {}
        args = SimpleNamespace(stop=False, check_api=False, goal='Move then attack', max_turns=2)
        try:
            with patch.object(agent, 'chat', side_effect=lambda *a: release.wait(3)):
                agent.run(args, report)
        finally:
            release.set()
        self.assertEqual(report['outcome'], 'interrupted')
        self.assertEqual(self.game.commands, [])


class InterruptionTests(unittest.TestCase):
    """Suspension, unreachable bridge and lost replies must not look like ordinary failures."""

    def setUp(self):
        self.game = FakeGame()
        self.control = agent.TaskControl(copy.deepcopy(self.game.state))
        self.requests = patch.object(agent, 'game_request', self.game.request)
        self.requests.start()
        self.addCleanup(self.requests.stop)

    def execute(self, commands):
        with patch.object(agent.time, 'sleep'):
            return agent.execute_actions(commands, 0, self.control)

    def test_paused_game_is_reported_as_paused_not_generic_unknown(self):
        self.game.state['paused'] = True
        with patch.object(agent, 'RESUME_TIMEOUT', 0.2):
            result = self.execute(['/tpa right 0.5', '/tpa attack right'])
        self.assertEqual(result['outcome'], 'unknown')
        self.assertEqual(result['interruption'], 'paused')
        self.assertFalse(result['resume']['resumed'])
        self.assertEqual(result['actions'], [])
        self.assertEqual(self.game.commands, [])

    def test_frozen_tick_is_reported_separately_from_pause(self):
        original = self.game.request

        def frozen(command, expected_revision=None):
            actor = self.game.state['actor']
            ticks = (actor['moveTicks'], actor['jumpTicks'], actor['attackTicks'])
            reply = original(command, expected_revision)
            if command == 'status':
                self.game.state['tick'] = 100
                reply['state']['tick'] = 100
                actor.update(moveTicks=ticks[0], jumpTicks=ticks[1], attackTicks=ticks[2])
                reply['state']['actor'].update(moveTicks=ticks[0], jumpTicks=ticks[1], attackTicks=ticks[2])
            return reply

        with patch.object(agent, 'game_request', frozen), \
                patch.object(agent, 'TICK_STALL_SECONDS', 0.05), \
                patch.object(agent, 'RESUME_TIMEOUT', 0.15):
            result = self.execute(['/tpa right 0.5'])
        self.assertEqual(result['interruption'], 'tick_stalled')
        self.assertEqual(result['actions'][0]['status'], 'unknown')


    def _suspend_then(self, change, commands=('/tpa right 2', '/tpa attack right')):
        original = self.game.request
        calls = [0]

        def wrapper(command, expected_revision=None):
            if command.startswith('/tpa right'):
                self.game.state['paused'] = True
                self.game.hold_active = True
            reply = original(command, expected_revision)
            if command == 'status':
                calls[0] += 1
                if calls[0] == 3:
                    self.game.state['paused'] = False
                    change()
            return reply

        with patch.object(agent, 'game_request', wrapper), patch.object(agent, 'RESUME_TIMEOUT', 0.5):
            return self.execute(list(commands))

    def test_resume_takes_over_an_action_that_is_still_running(self):
        result = self._suspend_then(lambda: None)
        self.assertEqual(result['interruption'], 'paused')
        self.assertTrue(result['resume']['resumed'])
        self.assertTrue(result['resume']['takeover'])
        self.assertEqual([c[0] for c in self.game.commands], ['/tpa right 2', '/tpa stop'])
        self.assertEqual(self.control.revision, 1)

    def test_user_stop_during_suspension_cancels_old_task(self):
        result = self._suspend_then(lambda: self.game.state.update(stopRevision=1))
        self.assertEqual(result['outcome'], 'interrupted')
        self.assertEqual(result['interruption'], 'cancelled')
        self.assertEqual([c[0] for c in self.game.commands], ['/tpa right 2'])

    def test_world_exit_during_suspension_cancels_old_task(self):
        result = self._suspend_then(lambda: self.game.state.update(gameMenu=True))
        self.assertEqual(result['outcome'], 'interrupted')
        self.assertEqual(result['interruption'], 'cancelled')

    def test_ai_replacement_during_suspension_cancels_old_task(self):
        result = self._suspend_then(lambda: self.game.state['actor'].update(slot=2))
        self.assertEqual(result['outcome'], 'interrupted')
        self.assertEqual(result['interruption'], 'cancelled')

    def test_resume_records_advanced_navigation_sequence_without_claiming_success(self):
        nav = dict(active=True, sequence=1, status='running', targetX=200)
        self.game.state['actor']['navigation'] = nav
        result = self._suspend_then(
            lambda: self.game.state['actor'].update(navigation=dict(nav, active=False, sequence=2, status='arrived')))
        evidence = result['evidence']['navigation']
        self.assertEqual((evidence['before'], evidence['after'], evidence['advanced']), (1, 2, True))
        self.assertEqual(result['actions'][0]['status'], 'unknown')

    def test_resume_records_unchanged_sequence_as_not_advanced(self):
        nav = dict(active=True, sequence=1, status='running', targetX=200)
        self.game.state['actor']['navigation'] = nav
        result = self._suspend_then(
            lambda: self.game.state['actor'].update(navigation=dict(nav, active=False)))
        evidence = result['evidence']['navigation']
        self.assertEqual((evidence['before'], evidence['after'], evidence['advanced']), (1, 1, False))
        self.assertEqual(result['actions'][0]['status'], 'unknown')

    def test_report_keeps_action_interruption_details(self):
        report = {}
        args = SimpleNamespace(stop=False, check_api=False, goal='Walk', max_turns=2)

        def chat(messages, tools=None):
            self.game.state['paused'] = True
            return {"choices": [{"message": {"role": "assistant", "content": "", "tool_calls": [
                {"id": "c1", "function": {"name": "game_actions",
                                          "arguments": json.dumps({"commands": ["/tpa right 1"]})}}]}}]}

        with patch.object(agent, 'chat', side_effect=chat), patch.object(agent, 'RESUME_TIMEOUT', 0.2):
            agent.run(args, report)
        self.assertEqual(report['outcome'], 'unknown')
        self.assertEqual(report['interruption'], 'paused')
        self.assertFalse(report['resume']['resumed'])

    def test_unconfirmed_stop_is_reported_instead_of_silently_dropped(self):
        report = {}
        args = SimpleNamespace(stop=False, check_api=False, goal='Walk', max_turns=2)
        original = self.game.request

        def no_stop(command, expected_revision=None):
            if command == '/tpa stop':
                raise agent.Unreachable('stop not answered', executed=False)
            return original(command, expected_revision)

        with patch.object(agent, 'game_request', no_stop), \
                patch.object(agent, 'chat_interruptibly', side_effect=agent.TaskInterrupted('user stop')), \
                patch.object(agent, 'RESUME_TIMEOUT', 0.2):
            agent.run(args, report)
        self.assertEqual(report['outcome'], 'interrupted')
        self.assertIn('stop_error', report)


class BridgeProtocolTests(unittest.TestCase):
    """The file mailbox must say whether an unanswered request may have run."""

    def setUp(self):
        # Some sandboxes only allow writes in directories created outside this process.
        base = Path(os.environ.get('TPA_TEST_MAILBOX') or tempfile.mkdtemp())
        self.directory = base / f"case-{uuid.uuid4().hex[:8]}"
        try:
            self.directory.mkdir()
            (self.directory / 'probe').write_text('', encoding='utf-8')
            (self.directory / 'probe').unlink()
        except OSError:
            self.skipTest('mailbox is not writable here; set TPA_TEST_MAILBOX to a writable directory')
        self.addCleanup(shutil.rmtree, self.directory, ignore_errors=True)
        self.mailbox_patch = patch.object(agent, 'MAILBOX', self.directory)
        self.mailbox_patch.start()
        self.addCleanup(self.mailbox_patch.stop)
        self.timeout_patch = patch.object(agent, 'REQUEST_TIMEOUT', 0.2)
        self.timeout_patch.start()
        self.addCleanup(self.timeout_patch.stop)

    def test_unanswered_request_is_removed_and_reported_as_not_executed(self):
        with self.assertRaises(agent.Unreachable) as caught:
            agent.game_request('/tpa right 1', 7)
        self.assertFalse(caught.exception.executed)
        self.assertEqual(list(self.directory.iterdir()), [])

    def test_consumed_request_without_reply_stays_unknown(self):
        seen = {}

        def consume():
            pending = self.directory / 'request.json'
            deadline = time.monotonic() + 2
            while time.monotonic() < deadline:
                if pending.exists():
                    seen.update(json.loads(pending.read_text(encoding='utf-8')))
                    pending.unlink()
                    return
                time.sleep(0.01)

        threading.Thread(target=consume, daemon=True).start()
        with self.assertRaises(agent.OutcomeUnknown):
            agent.game_request('/tpa mine 10 20', 3)
        self.assertEqual(seen['command'], '/tpa mine 10 20')
        self.assertEqual(seen['expectedStopRevision'], 3)
        self.assertEqual(list(self.directory.iterdir()), [])


class HostTests(unittest.TestCase):
    """The host serves one goal at a time and must stay honest about each one."""

    def setUp(self):
        base = Path(os.environ.get('TPA_TEST_MAILBOX') or tempfile.mkdtemp())
        self.mailbox = base / f"host-{uuid.uuid4().hex[:8]}"
        try:
            self.mailbox.mkdir()
            (self.mailbox / 'probe').write_text('', encoding='utf-8')
            (self.mailbox / 'probe').unlink()
        except OSError:
            self.skipTest('mailbox is not writable here; set TPA_TEST_MAILBOX to a writable directory')
        self.addCleanup(shutil.rmtree, self.mailbox, ignore_errors=True)
        self.mailbox_patch = patch.object(agent, 'MAILBOX', self.mailbox)
        self.mailbox_patch.start()
        self.addCleanup(self.mailbox_patch.stop)
        self.game = FakeGame()
        self.requests = patch.object(agent, 'game_request', self.game.request)
        self.requests.start()
        self.addCleanup(self.requests.stop)
        self.idle_patch = patch.object(agent, 'IDLE_POLL_SECONDS', 0.01)
        self.idle_patch.start()
        self.addCleanup(self.idle_patch.stop)
        self.args = SimpleNamespace(stop=False, check_api=False, goal=None, goal_file='', max_turns=2)

    def post(self, goal_id, text='Walk right'):
        path = self.mailbox / 'goal.json'
        temporary = path.with_suffix('.tmp')
        temporary.write_text(json.dumps({'id': goal_id, 'text': text}), encoding='utf-8')
        temporary.replace(path)

    def wait_for(self, name, timeout=5):
        path = self.mailbox / name
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            if path.exists():
                return json.loads(path.read_text(encoding='utf-8-sig'))
            time.sleep(0.01)
        self.fail(f'{name} was not written')

    def test_idle_host_makes_no_model_requests(self):
        report = {}
        with patch.object(agent, 'chat', side_effect=AssertionError('model called while idle')):
            agent.host(self.args, report, idle_polls=3)
        self.assertEqual(report['goals'], [])
        self.assertEqual(report['outcome'], 'host_stopped')

    def test_leftover_goal_is_marked_stale_and_not_run(self):
        self.post('old')
        report = {}
        with patch.object(agent, 'chat', side_effect=AssertionError('stale goal ran')):
            agent.host(self.args, report, idle_polls=1)
        self.assertEqual(self.wait_for('goal-ack-old-stale.json')['status'], 'stale')
        self.assertEqual(report['goals'], [])

    def test_goal_is_accepted_executed_and_reported(self):
        report = {}
        reply = {'choices': [{'message': {'role': 'assistant', 'content': 'done'}}]}
        with patch.object(agent, 'chat', side_effect=lambda *a, **k: reply):
            self.post('g1')
            self.assertTrue(agent.host_tick(self.args, report, set()))
        self.assertEqual(self.wait_for('goal-ack-g1-accepted.json')['status'], 'accepted')
        self.assertEqual(self.wait_for('goal-result-g1.json')['status'], 'finished')
        self.assertEqual(report['goals'][0]['status'], 'finished')

    def test_duplicate_goal_id_is_not_restarted(self):
        report = {}
        seen = set()
        calls = []

        def chat(messages, tools=None):
            calls.append(1)
            return {'choices': [{'message': {'role': 'assistant', 'content': 'done'}}]}

        with patch.object(agent, 'chat', side_effect=chat):
            self.post('g1')
            agent.host_tick(self.args, report, seen)
            self.post('g1')
            agent.host_tick(self.args, report, seen)
        self.assertEqual(self.wait_for('goal-ack-g1-duplicate.json')['status'], 'duplicate')
        self.assertEqual(len(calls), 1)
        self.assertEqual(len(report['goals']), 1)
        self.assertEqual(report['goals'][0]['status'], 'finished')

    def test_second_goal_replaces_the_running_one(self):
        report = {}
        seen = set()
        first = []

        def chat(messages, tools=None):
            if not first:
                first.append(1)
                self.post('g2', 'Mine stone')
                self.game.state['stopRevision'] += 1
                return {'choices': [{'message': {'role': 'assistant', 'content': 'old goal aborted'}}]}
            return {'choices': [{'message': {'role': 'assistant', 'content': 'new goal done'}}]}

        with patch.object(agent, 'chat', side_effect=chat):
            self.post('g1', 'Walk right')
            agent.host_tick(self.args, report, seen)
            agent.host_tick(self.args, report, seen)
        self.assertEqual([g['id'] for g in report['goals']], ['g1', 'g2'])
        self.assertEqual(report['goals'][0]['status'], 'interrupted')
        self.assertEqual(report['goals'][1]['status'], 'finished')
        self.assertEqual(self.wait_for('goal-result-g1.json')['status'], 'interrupted')
        self.assertEqual(self.wait_for('goal-result-g2.json')['status'], 'finished')

    def test_turn_budget_is_per_goal(self):
        report = {}
        seen = set()

        def chat(messages, tools=None):
            return {'choices': [{'message': {'role': 'assistant', 'content': '', 'tool_calls': [
                {'id': 'c', 'function': {'name': 'game_actions',
                                         'arguments': json.dumps({'commands': ['status']})}}]}}]}

        with patch.object(agent, 'chat', side_effect=chat):
            self.post('g1')
            agent.host_tick(self.args, report, seen)
            self.post('g2')
            agent.host_tick(self.args, report, seen)
        self.assertEqual([g['turns'] for g in report['goals']], [2, 2])
        self.assertEqual([g['status'] for g in report['goals']], ['budget_exhausted', 'budget_exhausted'])


    def test_mailbox_keeps_only_the_newest_unread_goal(self):
        self.post('g1', 'Walk right')
        self.post('g2', 'Mine stone')
        goal = agent.poll_goal()
        self.assertEqual(goal['id'], 'g2')
        self.assertIsNone(agent.poll_goal())

    def test_empty_goal_is_rejected_and_not_run(self):
        report = {}
        with patch.object(agent, 'chat', side_effect=AssertionError('rejected goal ran')):
            self.post('bad', '   ')
            self.assertTrue(agent.host_tick(self.args, report, set()))
        self.assertEqual(self.wait_for('goal-ack-bad-rejected.json')['status'], 'rejected')
        self.assertEqual(report['goals'], [])

    def test_host_loop_serves_goal_then_returns_to_idle(self):
        report = {}
        pending = [{'id': 'g1', 'text': 'Walk right'}]
        reply = {'choices': [{'message': {'role': 'assistant', 'content': 'done'}}]}
        with patch.object(agent, 'poll_goal', side_effect=lambda: pending.pop(0) if pending else None), \
                patch.object(agent, 'chat', side_effect=lambda *a, **k: reply):
            agent.host(self.args, report, idle_polls=2, max_goals=1)
        self.assertEqual(len(report['goals']), 1)
        self.assertEqual(report['goals'][0]['status'], 'finished')

    def test_failed_goal_does_not_stop_the_host(self):
        report = {}
        seen = set()
        calls = []

        def chat(messages, tools=None):
            calls.append(1)
            if len(calls) == 1:
                raise RuntimeError('API exploded')
            return {'choices': [{'message': {'role': 'assistant', 'content': 'done'}}]}

        with patch.object(agent, 'chat', side_effect=chat):
            self.post('g1')
            agent.host_tick(self.args, report, seen)
            self.post('g2')
            agent.host_tick(self.args, report, seen)
        self.assertEqual([g['status'] for g in report['goals']], ['failed', 'finished'])


if __name__ == '__main__':
    unittest.main(verbosity=2)
