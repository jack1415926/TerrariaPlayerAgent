"""Small external Agent using the existing development JSON bridge."""
import argparse
import json
import math
import os
from pathlib import Path
import re
import sys
import threading
import time
from urllib import error, request
import uuid

ROOT = Path(__file__).resolve().parents[1]
MAILBOX = ROOT / ".local/tModLoader/TPA"
COMMAND = re.compile(
    r"(?:status|/tpa (?:spawn|status|stop|jump|clear-enemies)|"
    r"/tpa equip (?:wood|iron|pick)|/tpa select [0-9]{1,2}|"
    r"/tpa mine [0-9]{1,5} [0-9]{1,5}(?: [0-9]+(?:\.[0-9]+)?)?|"
    r"/tpa goto [0-9]+(?:\.[0-9]+)?(?: [0-9]+(?:\.[0-9]+)?)?|"
    r"/tpa home(?: [0-9]+(?:\.[0-9]+)?)?|"
    r"/tpa enemy (?:slime|zombie|eye)|"
    r"/tpa (?:left|right|down|(?:attack|jump) (?:left|right))(?: [0-9]+(?:\.[0-9]+)?)?)"
)
TOOL = {
    "type": "function", "function": {
        "name": "game_actions",
        "description": (
            "按顺序完成一小批游戏动作，逐条返回实际结果和最新状态。"
            "commands中每条保留/tpa前缀，例如/tpa left 1；只有status可以不带前缀。"
            "优先用/tpa goto 水平世界像素X [最多秒数]到指定位置，或/tpa home [最多秒数]回活动起点；默认最多10秒。"
            "本地按实际位置减速停止，水平误差8像素且落地低速才arrived，卡住或超时会报告失败。"
            "actor.navigation含homeX/minX/maxX和目标结果；活动范围由宿主设置，模型不能修改。"
            "支持 status、/tpa spawn、/tpa equip wood或iron或pick、/tpa enemy slime或zombie或eye、"
            "/tpa select 背包槽位0-49、/tpa mine 世界方块X 世界方块Y [秒数]。"
            "equip pick发测试铜镐（slot1），不会替换slot0的剑；select只选择已有物品。"
            "actor.inventory提供非空物品槽位，mineCandidates提供附近方块坐标和类型。"
            "mineCandidates已排除原生禁止破坏的支撑等方块；canKill=true不保证镐强度足够。"
            "nearbyHostiles提供480像素内最近8个敌怪的位置、dx/dy、距离与接触状态；近敌优先短攻击或退避，不站着挖。"
            "挖掘需要先选择镐，默认最多2秒，最多10秒；沿用原生范围、工具强度与冷却，不保证挖得动所有方块。"
            "挖掘会等目标被原生PickTile移除或超时才返回；只有mining.status=broken及对应事件才能确认挖掉。"
            "/tpa left或right [秒数]、/tpa attack left或right [秒数]、/tpa jump [left或right [秒数]]、"
            "/tpa down [秒数]（默认0.25秒，原生平台下落）、/tpa stop、"
            "/tpa clear-enemies。移动/攻击默认2秒，最多10秒，新动作替换旧动作。"
            "每条移动/攻击/跳跃会等待输入结束后才执行下一条，不会互相覆盖。"
            "enemy在当前朝向前方生成；要测试命中，先生成敌人再攻击。"
            "wait_seconds是整批完成后额外等待的游戏模拟秒数。"
            "受阻或拒绝时停止该批后续动作；停止/移除会取消旧任务。"
            "状态actor.terrain提供附近17列11行地形、左右障碍与跳跃空间；行从上到下，列从左到右。"
            "jumpClearance只表示检测到可跨越的障碍；无障碍时也为false。头顶空间看headClear，不能从jumpClearance=false推断不能跳。"
            "移动会自动尝试跨坎，卡住会最多尝试两次跳跃调整并返回blockedReason。"
            "jump left/right是边走边跳；普通jump不清除已有移动。down不能穿过实心方块。"
            "不要只凭回复文字认为成功，查看返回的实际状态和命中事件。"
        ),
        "parameters": {
            "type": "object", "properties": {
                "commands": {"type": "array", "items": {"type": "string"}, "maxItems": 6},
                "wait_seconds": {"type": "number", "minimum": 0, "maximum": 15},
            }, "required": ["commands"], "additionalProperties": False,
        },
    },
}


def validate_actions(arguments):
    if not isinstance(arguments, dict) or set(arguments) - {"commands", "wait_seconds"}:
        raise ValueError("Expected commands and optional wait_seconds.")
    commands = arguments.get("commands")
    if not isinstance(commands, list) or len(commands) > 6:
        raise ValueError("commands must contain at most six commands.")
    for command in commands:
        if not isinstance(command, str) or not COMMAND.fullmatch(command):
            raise ValueError("Unsupported game command.")
        parts = command.split()
        if len(parts) > 1 and parts[1] == "select" and int(parts[2]) >= 50:
            raise ValueError("Inventory slot must be between 0 and 49.")
        if parts[0] == "/tpa" and (parts[1] in ("left", "right", "down", "attack", "mine", "goto", "home") or
                                       parts[1] == "jump" and len(parts) > 2):
            duration_index = 4 if parts[1] == "mine" else 3 if parts[1] in ("attack", "jump", "goto") else 2
            if len(parts) > duration_index and not 0 < float(parts[duration_index]) <= 10:
                raise ValueError("Movement/attack duration must be > 0 and <= 10 seconds.")
    wait = arguments.get("wait_seconds", 0.5)
    if type(wait) not in (int, float) or not math.isfinite(wait) or not 0 <= wait <= 15:
        raise ValueError("wait_seconds must be between 0 and 15.")
    return commands, wait


class TaskInterrupted(RuntimeError):
    def __init__(self, message, state=None):
        super().__init__(message)
        self.state = state


class OutcomeUnknown(RuntimeError):
    pass


class GameSuspended(RuntimeError):
    """The game reports paused: the task is suspended, not cancelled."""


class TickStalled(RuntimeError):
    """The bridge answers, but the world tick is not advancing."""


class Unreachable(RuntimeError):
    """The game did not answer. executed tells whether the request may have run."""

    def __init__(self, message, executed=False):
        super().__init__(message)
        self.executed = executed


INTERRUPTIONS = (TaskInterrupted, GameSuspended, TickStalled, Unreachable, OutcomeUnknown)
REQUEST_TIMEOUT = 5
RESUME_TIMEOUT = 15
TICK_STALL_SECONDS = 2


def _active(actor):
    if not actor:
        return False
    if any(actor.get(key, 0) > 0 for key in
           ("moveTicks", "jumpTicks", "attackTicks", "dropTicks", "mineTicks", "itemAnimation")):
        return True
    return bool((actor.get("navigation") or {}).get("active"))


def game_request(command, expected_revision=None):
    directory = MAILBOX
    if not directory.is_dir():
        raise RuntimeError("Start scripts/play.ps1 and enter the test world first.")
    pending = directory / ("stop.json" if command == "/tpa stop" else "request.json")
    if pending.exists():
        raise RuntimeError("Another game request is pending; use only one controller.")
    identity = str(uuid.uuid4())
    temporary = directory / f"{identity}.tmp"
    response = directory / f"response-{identity}.json"
    temporary.write_text(json.dumps({"id": identity, "command": command,
                                    "expectedStopRevision": expected_revision}), encoding="utf-8")
    try:
        # Windows rename refuses to overwrite another controller's pending request.
        temporary.rename(pending)
    finally:
        temporary.unlink(missing_ok=True)
    deadline = time.monotonic() + REQUEST_TIMEOUT
    while time.monotonic() < deadline:
        if response.exists():
            result = json.loads(response.read_text(encoding="utf-8-sig"))
            response.unlink()
            if result.get("id") != identity or not result.get("ok"):
                raise RuntimeError(result.get("error", "Invalid bridge response."))
            if result.get("interrupted"):
                raise TaskInterrupted(result["reply"], observation(result))
            return result
        time.sleep(0.05)
    if pending.exists() and json.loads(pending.read_text(encoding="utf-8-sig")).get("id") == identity:
        pending.unlink()
        raise Unreachable("The game did not answer; the request was not executed.", executed=False)
    raise OutcomeUnknown("The game took the request but did not answer; its outcome is unknown.")


def observation(result):
    state = result["state"]
    compact = {key: state.get(key) for key in ("gameMenu", "netMode", "paused", "tick", "stopRevision", "player", "actor")}
    if compact["actor"]:
        compact["actor"] = dict(compact["actor"])
        compact["actor"]["combatEvents"] = compact["actor"].get("combatEvents", [])[-12:]
    return compact


class TaskControl:
    def __init__(self, state):
        if state.get("stopRevision") is None:
            raise RuntimeError("Rebuild/reload the mod: this Agent requires stopRevision support.")
        self.revision = state["stopRevision"]
        self.state = state
        self.events = []
        self.takeover = False
        actor = state.get("actor") or {}
        self.actor_slot = actor.get("slot")
        self.actor_death = (actor.get("deathCount"), actor.get("respawnCount"))

    def update(self, reply):
        self.events = (reply["state"].get("actor") or {}).get("combatEvents", [])
        self.state = observation(reply)
        actor = self.state.get("actor") or {}
        if actor.get("slot") is not None:
            self.actor_slot = actor.get("slot")
            self.actor_death = (actor.get("deathCount"), actor.get("respawnCount"))

    def observe(self):
        self.update(game_request("status"))
        if self.state["stopRevision"] != self.revision:
            raise TaskInterrupted("User stopped/removed the AI; old task cancelled.")
        if self.state["gameMenu"] or self.state["netMode"] != 0:
            raise TaskInterrupted("The single-player world is no longer running.")
        if self.state["paused"]:
            raise GameSuspended("The game is paused; no new command was dispatched.")
        return self.state

    def resume(self):
        """Wait out a pause or outage, then prove this task still owns the AI."""
        deadline = time.monotonic() + RESUME_TIMEOUT
        while time.monotonic() <= deadline:
            try:
                state = observation(game_request("status"))
            except (Unreachable, OutcomeUnknown):
                time.sleep(0.1)
                continue
            if state["stopRevision"] != self.revision:
                raise TaskInterrupted("User stopped/removed the AI while the task was suspended.")
            if state["gameMenu"] or state["netMode"] != 0:
                raise TaskInterrupted("The single-player world is no longer running.")
            actor = state.get("actor") or {}
            if (actor.get("slot") != self.actor_slot or
                    (actor.get("deathCount"), actor.get("respawnCount")) != self.actor_death):
                raise TaskInterrupted("The AI was removed, died or replaced while the task was suspended.")
            self.state = state
            if state["paused"]:
                time.sleep(0.1)
                continue
            if not _active(actor):
                return state
            if not self.takeover:
                # A stale input is still running: cancel it before this task stops dispatching.
                self.update(game_request("/tpa stop", self.revision))
                self.revision = self.state["stopRevision"]
                self.takeover = True
            time.sleep(0.1)
        raise OutcomeUnknown("The game did not recover in time; the action outcome stays unknown.")

    def recover(self, exc):
        """Classify an interruption, try to reclaim the task, and keep the evidence."""
        kind = ("cancelled" if isinstance(exc, TaskInterrupted) else
                "paused" if isinstance(exc, GameSuspended) else
                "tick_stalled" if isinstance(exc, TickStalled) else
                "unreachable" if isinstance(exc, Unreachable) else "unknown")
        record = {"kind": kind, "error": str(exc), "executed": getattr(exc, "executed", None), "resume": None}
        if kind == "cancelled":
            if getattr(exc, "state", None) is not None:
                self.state = exc.state
            return record
        try:
            self.state = self.resume()
        except TaskInterrupted as cancelled:
            record.update(kind="cancelled", error=str(cancelled), resume={"resumed": False, "cancelled": True})
            self.state = cancelled.state or self.state
        except (Unreachable, GameSuspended, TickStalled, OutcomeUnknown) as failed:
            record["resume"] = {"resumed": False, "reason": str(failed)}
        else:
            record["resume"] = {"resumed": True, "takeover": self.takeover}
        return record

    def wait(self, seconds=0, action=False, actor_before=None):
        start = self.state["tick"]
        last_tick, progressed = start, time.monotonic()
        deadline = progressed + seconds + 20
        while True:
            state = self.state
            actor = state.get("actor")
            if actor_before and (not actor or actor["dead"] or
                    any(actor[key] != actor_before[key] for key in ("deathCount", "respawnCount"))):
                raise TaskInterrupted("AI died or was replaced; remaining actions cancelled.")
            active = action and _active(actor)
            if not active and state["tick"] - start >= math.ceil(seconds * 60):
                return state
            now = time.monotonic()
            if state["tick"] != last_tick:
                last_tick, progressed = state["tick"], now
            if state["paused"]:
                raise GameSuspended("The game is paused; the action was suspended, not cancelled.")
            if now - progressed > TICK_STALL_SECONDS:
                raise TickStalled("The world tick stopped advancing; action completion is unknown.")
            if now > deadline:
                raise OutcomeUnknown("The action did not finish before its deadline; completion is unknown.")
            time.sleep(0.1)
            self.observe()


def chat_interruptibly(messages, control):
    # Only the network call runs in the daemon thread; game requests stay serialized here.
    finished = threading.Event()
    result = {}

    def fetch():
        try:
            result["reply"] = chat(messages, [TOOL])
        except Exception as exc:
            result["error"] = exc
        finally:
            finished.set()

    threading.Thread(target=fetch, daemon=True).start()
    while not finished.wait(0.2):
        control.observe()
    control.observe()  # A stop wins even when the API finishes at the same time.
    if "error" in result:
        raise result["error"]
    return result["reply"]


def execute_actions(commands, wait, control):
    actions = []
    try:
        return _execute_actions(commands, wait, control, actions)
    except INTERRUPTIONS as exc:
        record = control.recover(exc)
        cancelled = record["kind"] == "cancelled"
        if actions and actions[-1]["status"] == "running":
            actions[-1].update(status="interrupted" if cancelled else "unknown",
                               reason=record["error"], state=control.state,
                               interruption=record["kind"])
        return {"ok": False, "outcome": "interrupted" if cancelled else "unknown",
                "error": record["error"], "interruption": record["kind"],
                "resume": record["resume"], "actions": actions, "state": control.state}


def _execute_actions(commands, wait, control, actions):
    for command in commands:
        before = control.observe()
        entry = {"command": command, "start_tick": before["tick"], "status": "running"}
        actions.append(entry)
        reply = game_request(command, control.revision)
        control.update(reply)
        if command == "/tpa stop":
            control.revision = control.state["stopRevision"]
        entry["reply"] = reply["reply"]
        if not reply.get("accepted", False):
            entry.update(status="rejected", state=control.state)
            break
        parts = command.split()
        timed = len(parts) > 1 and parts[1] in ("left", "right", "attack", "jump", "down", "mine", "goto", "home")
        previous = before.get("actor")
        after = control.wait(action=True, actor_before=previous) if timed else control.state
        actor = after.get("actor")
        entry.update(status="completed", end_tick=after["tick"], state=after)
        if len(parts) > 1 and parts[1] in ("goto", "home"):
            nav = (actor or {}).get("navigation") or {}
            previous_nav = (before.get("actor") or {}).get("navigation") or {}
            entry["navigation"] = nav
            arrived = (nav.get("status") == "arrived" and nav.get("sequence") != previous_nav.get("sequence") and
                       nav.get("targetX") is not None and abs(actor["x"] - nav["targetX"]) <= 8 and
                       abs(actor.get("velocityX", 0)) <= 0.15)
            if not arrived:
                entry.update(status="blocked" if nav.get("status") == "blocked" else "unconfirmed",
                             reason=nav.get("reason") or "Target arrival was not confirmed.")
                break
        if len(parts) > 1 and parts[1] == "mine":
            mining = (actor or {}).get("mining") or {}
            previous_mining = (before.get("actor") or {}).get("mining") or {}
            confirmed = (mining.get("status") == "broken" and
                         mining.get("sequence") != previous_mining.get("sequence") and
                         mining.get("x") == int(parts[2]) and mining.get("y") == int(parts[3]) and
                         any(event.get("broken") and event.get("actor") == actor.get("slot")
                             and event.get("item") == mining.get("item") and
                             event.get("x") == int(parts[2]) and event.get("y") == int(parts[3])
                             for event in mining.get("events", [])))
            entry["mining"] = mining
            if not confirmed:
                entry.update(status="blocked" if mining.get("status") == "blocked" else "unconfirmed",
                             reason=mining.get("reason") or "No matching native pick break was confirmed.")
                break
        if timed and previous and actor:
            entry.update(deltaX=actor["x"] - previous["x"], deltaY=actor["y"] - previous["y"], input_finished=True)
            direction_name = parts[1] if parts[1] in ("left", "right") else (
                parts[2] if parts[1] == "jump" and len(parts) > 2 else None)
            if direction_name:
                direction = -1 if direction_name == "left" else 1
                if actor.get("blockedReason") or entry["deltaX"] * direction <= 2:
                    entry.update(status="blocked", reason=actor.get("blockedReason") or
                                 "No meaningful movement in the requested direction was observed.")
                    break
            if parts[1] == "down" and entry["deltaY"] <= 2:
                entry.update(status="blocked", reason="No downward progress observed; down only drops through platforms.")
                break
            if parts[1] == "attack":
                # Read the full bounded game event buffer before compacting it for the model.
                hits = [event for event in control.events
                        if event.get("kind") == "item-hit" and event.get("tick", 0) > before["tick"]
                        and event.get("attacker") == actor["slot"] and event.get("item") == actor["item"]
                        and event.get("damage", 0) > 0]
                entry["item_hits"] = hits
                if not hits:
                    entry.update(status="unconfirmed", reason="Attack input ended; no corresponding item-hit was observed.")
    state = control.wait(wait)
    return {"ok": all(action["status"] == "completed" for action in actions), "actions": actions, "state": state}


def chat(messages, tools=None, max_tokens=None):
    key = os.environ.get("TPA_API_KEY", "")
    if not key:
        raise RuntimeError("TPA_API_KEY is not configured.")
    payload = {
        "model": os.environ.get("TPA_API_MODEL", "deepseek-flash"),
        "thinking": {"type": os.environ.get("TPA_API_THINKING", "disabled")},
        "max_tokens": max_tokens or int(os.environ.get("TPA_API_MAX_TOKENS", "1024")),
        "messages": messages,
    }
    if tools:
        payload["tools"] = tools
    url = os.environ.get("TPA_API_BASE_URL", "https://api.deepseek.com").rstrip("/") + "/chat/completions"
    req = request.Request(url, json.dumps(payload, ensure_ascii=False).encode("utf-8"), headers={
        "Authorization": f"Bearer {key}", "Content-Type": "application/json",
    })
    try:
        with request.urlopen(req, timeout=45) as reply:
            return json.load(reply)
    except error.HTTPError as exc:
        detail = exc.read().decode("utf-8", errors="replace").replace(key, "[REDACTED]")[:400]
        raise RuntimeError(f"API HTTP {exc.code}: {detail}") from None


def verify_weapon_task(report):
    evidence = {"wood": [], "iron": []}
    seen = set()
    for turn in report.get("turns", []):
        for tool in turn["tools"]:
            actor = (tool["result"].get("state") or {}).get("actor") or {}
            events = list(actor.get("combatEvents", []))
            for action in tool["result"].get("actions", []):
                events.extend(action.get("item_hits", []))
            for event in events:
                identity = (event.get("tick"), event.get("npc"), event.get("item"))
                if (event.get("kind") != "item-hit" or event.get("npcType") != 1 or
                    event.get("attacker") != actor.get("slot") or
                    event.get("tick", 0) <= report["initial_state"]["tick"] or identity in seen):
                    continue
                weapon = {24: "wood", 4: "iron"}.get(event.get("item"))
                if weapon and event.get("damage", 0) > 0:
                    seen.add(identity)
                    evidence[weapon].append(event)
    return {"passed": all(evidence.values()), "item_hits": evidence}


def run(args, report):
    if args.stop:
        result = game_request("/tpa stop")
        report.update(outcome="stopped", final_state=observation(result))
        print("Stop confirmed by game; active Agent task will be cancelled.", flush=True)
        return
    if args.check_api:
        reply = chat([{"role": "user", "content": "只回复OK"}], max_tokens=8)
        report.update(outcome="api_connected", model=reply.get("model"), usage=reply.get("usage"))
        print(f"API connected: model={reply.get('model')}, reply={reply['choices'][0]['message'].get('content')}", flush=True)
        return
    initial = observation(game_request("status"))
    if initial["gameMenu"] or initial["netMode"] != 0 or initial["paused"]:
        raise RuntimeError("Enter a running single-player test world and keep it focused.")
    if not args.goal and Path(args.goal_file).resolve() == ROOT / "agent/prompts/explore.md":
        if not initial.get("actor") or initial["actor"]["dead"]:
            raise RuntimeError("Prepare a living exploration AI and its starting equipment first.")
        prepared = game_request("/tpa range 25", initial["stopRevision"])
        if not prepared.get("accepted"):
            raise RuntimeError(prepared["reply"])
        initial = observation(prepared)
        report["activity_range_setup"] = prepared["reply"]
    goal = args.goal or Path(args.goal_file).read_text(encoding="utf-8")
    report.update(goal=goal, initial_state=initial, turns=[], outcome="budget_exhausted")
    control = TaskControl(initial)
    messages = [
        {"role": "system", "content": (ROOT / "agent/prompts/system.md").read_text(encoding="utf-8")},
        {"role": "user", "content": goal + "\n初始状态：\n" + json.dumps(initial, ensure_ascii=False)},
    ]
    try:
        for turn in range(1, args.max_turns + 1):
            print(f"Model request {turn}/{args.max_turns}", flush=True)
            reply = chat_interruptibly(messages, control)
            message = reply["choices"][0]["message"]
            entry = {"model": reply.get("model"), "usage": reply.get("usage"), "assistant": message, "tools": []}
            report["turns"].append(entry)
            messages.append(message)
            if message.get("content"):
                print(message["content"], flush=True)
            calls = message.get("tool_calls", [])
            if not calls:
                report.update(outcome="model_finished", summary=message.get("content", ""))
                break
            if len(calls) > 4:
                raise RuntimeError("Too many tool batches in one response.")
            for call in calls:
                try:
                    if call["function"]["name"] != "game_actions":
                        raise ValueError("Unknown tool.")
                    commands, wait = validate_actions(json.loads(call["function"]["arguments"]))
                except (ValueError, TypeError) as exc:
                    result = {"ok": False, "error": str(exc)}
                else:
                    print(f"Model actions: {commands}; wait={wait}s", flush=True)
                    result = execute_actions(commands, wait, control)
                    for action in result["actions"]:
                        print(f"Action {action['command']}: {action['status']}; "
                              f"deltaX={action.get('deltaX')}, item_hits={len(action.get('item_hits', []))}", flush=True)
                        if action.get("mining"):
                            mining = action["mining"]
                            print(f"Mining ({mining.get('x')},{mining.get('y')}): {mining.get('status')}; "
                                  f"attempts={mining.get('attempts')}, reason={mining.get('reason')}", flush=True)
                    actor = result["state"].get("actor") or {}
                    print(f"Observed: item={actor.get('item')}, hits={actor.get('hitCount')}, life={actor.get('life')}", flush=True)
                entry["tools"].append({"call_id": call["id"], "result": result})
                messages.append({"role": "tool", "tool_call_id": call["id"], "content": json.dumps(result, ensure_ascii=False)})
                if result.get("outcome") in ("interrupted", "unknown"):
                    report.update(outcome=result["outcome"], error=result["error"],
                                  interruption=result.get("interruption"), resume=result.get("resume"))
                    return
    except INTERRUPTIONS as exc:
        record = control.recover(exc)
        report.update(outcome="interrupted" if record["kind"] == "cancelled" else "unknown",
                      error=record["error"], interruption=record["kind"], resume=record["resume"])
        print(record["error"], flush=True)
    finally:
        # Preserve the actor and default respawn; only cancel active input.
        try:
            # Never let an old task's cleanup stop a newer task after a user cancellation.
            report["final_state"] = observation(game_request("/tpa stop", control.revision))
        except TaskInterrupted as exc:
            report["final_state"] = exc.state or control.state
        except RuntimeError as exc:
            report["stop_error"] = str(exc)
            print(f"Stop outcome unknown: {exc}", file=sys.stderr)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check-api", action="store_true")
    parser.add_argument("--stop", action="store_true")
    parser.add_argument("--goal")
    parser.add_argument("--goal-file", default=str(ROOT / "agent/prompts/weapon-test.md"))
    parser.add_argument("--max-turns", type=int, default=int(os.environ.get("TPA_AGENT_MAX_TURNS", "8")))
    args = parser.parse_args()
    if not 1 <= args.max_turns <= 30:
        parser.error("--max-turns must be between 1 and 30")
    report = {"outcome": "failed"}
    exit_code = 0
    try:
        run(args, report)
        if report["outcome"] in ("budget_exhausted", "unknown"):
            exit_code = 1
        if not args.stop and not args.check_api and not args.goal and Path(args.goal_file).resolve() == ROOT / "agent/prompts/weapon-test.md":
            report["task_verification"] = verify_weapon_task(report)
            if not report["task_verification"]["passed"]:
                if report["outcome"] == "model_finished":
                    report["outcome"] = "task_incomplete"
                print("Task incomplete: required weapon item-hit evidence is missing.", flush=True)
                exit_code = 1
    except (Exception, KeyboardInterrupt) as exc:
        report.update(outcome="failed", error=str(exc))
        print(f"Agent stopped: {exc}", file=sys.stderr)
        exit_code = 1
    directory = ROOT / ".local/test-results"
    directory.mkdir(parents=True, exist_ok=True)
    path = directory / f"agent-{time.strftime('%Y%m%d-%H%M%S')}-{uuid.uuid4().hex[:6]}.json"
    path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Report: {path}", flush=True)
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
