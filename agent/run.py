"""Small external Agent using the existing development JSON bridge."""
import argparse
import json
import math
import os
from pathlib import Path
import re
import sys
import time
from urllib import error, request
import uuid

ROOT = Path(__file__).resolve().parents[1]
COMMAND = re.compile(
    r"(?:status|/tpa (?:spawn|status|stop|jump|clear-enemies)|"
    r"/tpa equip (?:wood|iron)|/tpa enemy (?:slime|zombie|eye)|"
    r"/tpa (?:left|right|attack (?:left|right))(?: [0-9]+(?:\.[0-9]+)?)?)"
)
TOOL = {
    "type": "function", "function": {
        "name": "game_actions",
        "description": (
            "按顺序执行一小批游戏动作，然后等待并返回最新状态。"
            "支持 status、/tpa spawn、/tpa equip wood或iron、/tpa enemy slime或zombie或eye、"
            "/tpa left或right [秒数]、/tpa attack left或right [秒数]、/tpa jump、/tpa stop、"
            "/tpa clear-enemies。移动/攻击默认2秒，最多10秒，新动作替换旧动作。"
            "enemy在当前朝向前方生成；一次批量内先发攻击再生成敌人可以减少调用延迟影响。"
            "批量命令之间不等待动作完成：移动后立刻攻击会取消移动。要先移动到位，"
            "请单独提交移动并等待对应时长，下一次调用再攻击。"
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
        if parts[0] == "/tpa" and parts[1] in ("left", "right", "attack"):
            duration_index = 3 if parts[1] == "attack" else 2
            if len(parts) > duration_index and not 0 < float(parts[duration_index]) <= 10:
                raise ValueError("Movement/attack duration must be > 0 and <= 10 seconds.")
    wait = arguments.get("wait_seconds", 0.5)
    if type(wait) not in (int, float) or not math.isfinite(wait) or not 0 <= wait <= 15:
        raise ValueError("wait_seconds must be between 0 and 15.")
    return commands, wait


def game_request(command):
    directory = ROOT / ".local/tModLoader/TPA"
    if not directory.is_dir():
        raise RuntimeError("Start scripts/play.ps1 and enter the test world first.")
    pending = directory / "request.json"
    if pending.exists():
        raise RuntimeError("Another game request is pending; use only one controller.")
    identity = str(uuid.uuid4())
    temporary = directory / f"{identity}.tmp"
    response = directory / f"response-{identity}.json"
    temporary.write_text(json.dumps({"id": identity, "command": command}), encoding="utf-8")
    try:
        # Windows rename refuses to overwrite another controller's pending request.
        temporary.rename(pending)
    finally:
        temporary.unlink(missing_ok=True)
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        if response.exists():
            result = json.loads(response.read_text(encoding="utf-8-sig"))
            response.unlink()
            if result.get("id") != identity or not result.get("ok"):
                raise RuntimeError(result.get("error", "Invalid bridge response."))
            return result
        time.sleep(0.05)
    if pending.exists() and json.loads(pending.read_text(encoding="utf-8-sig")).get("id") == identity:
        pending.unlink()
    raise RuntimeError("Game request timed out. Outcome unknown; focus the game and query status before retrying.")


def observation(result):
    state = result["state"]
    compact = {key: state.get(key) for key in ("gameMenu", "netMode", "paused", "tick", "player", "actor")}
    if compact["actor"]:
        compact["actor"] = dict(compact["actor"])
        compact["actor"]["combatEvents"] = compact["actor"].get("combatEvents", [])[-12:]
    return compact


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
            for event in actor.get("combatEvents", []):
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
    if args.check_api:
        reply = chat([{"role": "user", "content": "只回复OK"}], max_tokens=8)
        report.update(outcome="api_connected", model=reply.get("model"), usage=reply.get("usage"))
        print(f"API connected: model={reply.get('model')}, reply={reply['choices'][0]['message'].get('content')}", flush=True)
        return
    initial = observation(game_request("status"))
    if initial["gameMenu"] or initial["netMode"] != 0 or initial["paused"]:
        raise RuntimeError("Enter a running single-player test world and keep it focused.")
    goal = args.goal or Path(args.goal_file).read_text(encoding="utf-8")
    report.update(goal=goal, initial_state=initial, turns=[], outcome="budget_exhausted")
    messages = [
        {"role": "system", "content": (ROOT / "agent/prompts/system.md").read_text(encoding="utf-8")},
        {"role": "user", "content": goal + "\n初始状态：\n" + json.dumps(initial, ensure_ascii=False)},
    ]
    try:
        for turn in range(1, args.max_turns + 1):
            print(f"Model request {turn}/{args.max_turns}", flush=True)
            reply = chat(messages, [TOOL])
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
                    replies = [{"command": command, "reply": game_request(command)["reply"]} for command in commands]
                    time.sleep(wait)
                    result = {"ok": True, "replies": replies, "state": observation(game_request("status"))}
                    actor = result["state"].get("actor") or {}
                    print(f"Observed: item={actor.get('item')}, hits={actor.get('hitCount')}, life={actor.get('life')}", flush=True)
                entry["tools"].append({"call_id": call["id"], "result": result})
                messages.append({"role": "tool", "tool_call_id": call["id"], "content": json.dumps(result, ensure_ascii=False)})
    finally:
        # Preserve the actor and default respawn; only cancel active input.
        try:
            report["final_state"] = observation(game_request("/tpa stop"))
        except RuntimeError as exc:
            report["stop_error"] = str(exc)
            print(f"Stop outcome unknown: {exc}", file=sys.stderr)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check-api", action="store_true")
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
        if report["outcome"] == "budget_exhausted":
            exit_code = 1
        if not args.check_api and not args.goal and Path(args.goal_file).resolve() == ROOT / "agent/prompts/weapon-test.md":
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
