"""Offline checks for sequential actions, cancellation and factual feedback."""
import copy
import threading
import time
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import run as agent


class FakeGame:
    def __init__(self):
        self.state = dict(gameMenu=False, netMode=0, paused=False, tick=100, stopRevision=0,
                          player={}, actor=dict(slot=1, x=100, y=100, life=100, dead=False,
                          deathCount=0, respawnCount=0, moveTicks=0, jumpTicks=0, attackTicks=0,
                          itemAnimation=0, item=24, combatEvents=[]))
        self.commands = []
        self.blocked = self.reject = self.cancel = False

    def request(self, command, expected_revision=None):
        if expected_revision is not None and expected_revision != self.state['stopRevision']:
            raise agent.TaskInterrupted('stale task')
        actor = self.state['actor']
        if command == 'status':
            self.state['tick'] += 6
            if self.cancel:
                self.state['stopRevision'] += 1
            for key in ('moveTicks', 'jumpTicks', 'attackTicks', 'itemAnimation'):
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
        result = self.execute(['/tpa right', '/tpa attack right'])
        self.assertEqual(result['outcome'], 'unknown')
        self.assertEqual(len(self.game.commands), 1)

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


if __name__ == '__main__':
    unittest.main(verbosity=2)
